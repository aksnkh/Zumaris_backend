using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.BuildingBlocks.Common.Validation;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Zumaris.Provider.Application;
using Zumaris.Provider.Infrastructure.Persistence;
using Zumaris.Provider.Infrastructure.Integrations;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json",optional:true,reloadOnChange:false)
    .AddEnvironmentVariables().AddCommandLine(args);
var identityUri = new Uri(builder.Configuration["Identity:BaseUrl"] ?? "http://127.0.0.1:5001/");
if (!identityUri.IsAbsoluteUri || (identityUri.Scheme != "https" && !(identityUri.Scheme == "http" && identityUri.IsLoopback)) ||
    !string.IsNullOrEmpty(identityUri.UserInfo) || !identityUri.AbsoluteUri.EndsWith('/'))
    throw new InvalidOperationException("Identity:BaseUrl must be HTTPS or loopback HTTP, with a trailing slash and no credentials.");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IProviderQuoteRepository,ProviderQuoteRepository>();
builder.Services.AddHealthChecks();
builder.Services.AddScoped<IProviderAccountRepository,ProviderAccountRepository>();
builder.Services.AddScoped<IProviderSignupRepository,ProviderSignupRepository>();
builder.Services.AddScoped<IProviderSignupClient,ProviderSignupClient>();
builder.Services.AddScoped<ProviderSignupService>();
builder.Services.AddScoped<Zumaris.Provider.Api.Controllers.ProviderSignupExceptionFilter>();
builder.Services.AddScoped<IProviderWorkflowClient,ProviderWorkflowClient>();
builder.Services.AddScoped<Zumaris.Provider.Api.Controllers.ProviderWorkflowFilter>();
builder.Services.AddScoped<Zumaris.Provider.Api.Controllers.ProviderAdminFilter>();
foreach(var service in new[]{("IdentityAdmin",identityUri), ("ServiceRequest",new Uri(builder.Configuration["ServiceRequest:BaseUrl"]??"http://127.0.0.1:5002/"))})
{
    var endpoint=service.Item2;
    if(!endpoint.IsAbsoluteUri||!string.IsNullOrEmpty(endpoint.UserInfo)||!endpoint.AbsoluteUri.EndsWith('/')||!(endpoint.Scheme=="https"||endpoint.Scheme=="http"&&endpoint.IsLoopback))
        throw new InvalidOperationException("Reference service URLs must use HTTPS or loopback HTTP and end with a slash.");
    builder.Services.AddHttpClient(service.Item1,client=>{client.BaseAddress=endpoint;client.Timeout=TimeSpan.FromSeconds(5);if(endpoint.IsLoopback)client.DefaultRequestHeaders.Add("X-Forwarded-Proto","https");})
        .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false});
}
builder.Services.AddHttpClient<IIdentitySessionValidator, IdentitySessionValidator>(client =>
{
    client.BaseAddress = identityUri;
    client.Timeout = TimeSpan.FromSeconds(5);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddProblemDetails();
builder.Services.AddControllers().AddValidationMessages().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    // User identity, current selection and summary statistics are server-owned fields.
    options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear(); options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback); options.KnownProxies.Add(IPAddress.IPv6Loopback);
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Provider API", Version = "v1" });
    c.AddServer(new OpenApiServer { Url = "../.." });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        { [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>() });
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false; options.IncludeErrorDetails = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
        ValidateIssuerSigningKey = true, RequireSignedTokens = true, RequireExpirationTime = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"], ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ValidTypes = ["JWT"], ClockSkew = TimeSpan.FromSeconds(15)
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var userId) || userId == Guid.Empty ||
                !Guid.TryParse(context.Principal?.FindFirst("sid")?.Value, out var sessionId) || sessionId == Guid.Empty)
            { context.Fail("Invalid identity claims."); return; }
            var bearer = context.Request.Headers.Authorization.ToString()["Bearer ".Length..];
            var validator = context.HttpContext.RequestServices.GetRequiredService<IIdentitySessionValidator>();
            try
            {
                if (!await validator.IsActiveAsync(bearer, context.Request.IsHttps, context.HttpContext.RequestAborted))
                    context.Fail("Session is not active.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                context.HttpContext.Items["IdentityUnavailable"] = true;
                context.Fail("Identity unavailable.");
            }
        },
        OnChallenge = async context =>
        {
            if(context.Request.Path.Equals("/api/providers/signup",StringComparison.OrdinalIgnoreCase))
            {
                context.HandleResponse();var status=context.HttpContext.Items.ContainsKey("IdentityUnavailable")?503:401;
                await Results.Json(ApiResponseError.Create(status,status==503?"Identity session validation is temporarily unavailable.":"Sign in to complete provider registration.",context.HttpContext.TraceIdentifier),options:new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web){Converters={new JsonStringEnumConverter<MessageLevel>()}},statusCode:status).ExecuteAsync(context.HttpContext);
                return;
            }
            if (context.HttpContext.Items.ContainsKey("IdentityUnavailable"))
            {
                context.HandleResponse();
                await Results.Problem(statusCode: 503, title: "Identity session validation is temporarily unavailable.")
                    .ExecuteAsync(context.HttpContext);
            }
        }
    };
});
builder.Services.AddAuthorization();
var app = builder.Build();
if (Encoding.UTF8.GetByteCount(app.Configuration["Jwt:Key"] ?? "") < 32 ||
    string.IsNullOrWhiteSpace(app.Configuration["Jwt:Issuer"]) || string.IsNullOrWhiteSpace(app.Configuration["Jwt:Audience"]))
    throw new InvalidOperationException("Run scripts/Initialize-ProviderLocalConfig.ps1 or configure the Identity JWT settings in environment variables.");
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("v1/swagger.json", "Provider API v1"));
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.UseCors(); app.UseAuthentication(); app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapControllers();
app.Run();
public partial class Program;
