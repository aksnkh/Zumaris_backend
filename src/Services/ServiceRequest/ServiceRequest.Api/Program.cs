using Zumaris.BuildingBlocks.Common.Validation;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Zumaris.ServiceRequests.Application;
using Zumaris.ServiceRequests.Infrastructure;
using Zumaris.ServiceRequests.Api.Controllers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<IISServerOptions>(options => options.MaxRequestBodySize = 44 * 1024 * 1024);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables().AddCommandLine(args);
var identityUri = new Uri(builder.Configuration["Identity:BaseUrl"] ?? "http://127.0.0.1:5001/");
if (!identityUri.IsAbsoluteUri || (identityUri.Scheme != "https" && !(identityUri.Scheme == "http" && identityUri.IsLoopback)) ||
    !string.IsNullOrEmpty(identityUri.UserInfo) || !identityUri.AbsoluteUri.EndsWith('/'))
    throw new InvalidOperationException("Identity:BaseUrl must be HTTPS or loopback HTTP, with a trailing slash and no credentials.");
var schedule = builder.Configuration.GetSection("Scheduling").Get<SchedulingSettings>() ?? new();
_ = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
builder.Services.AddSingleton(schedule);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IServiceRequestRepository, ServiceRequestAdoNetRepository>();
builder.Services.AddScoped<IServiceRequestService, ServiceRequestService>();
builder.Services.AddScoped<IServiceHistoryRepository, ServiceHistoryAdoNetRepository>();
builder.Services.AddScoped<IServiceHistoryService, ServiceHistoryService>();
builder.Services.AddScoped<IRequestLifecycleRepository, RequestLifecycleAdoNetRepository>();
builder.Services.AddScoped<IRequestManagementService, RequestManagementService>();
builder.Services.AddSingleton<IPhotoStorage, LocalPhotoStorage>();
builder.Services.AddScoped<ManagementExceptionFilter>();
builder.Services.AddHttpClient<IIdentitySessionValidator, IdentitySessionValidator>(client =>
{
    client.BaseAddress = identityUri;
    client.Timeout = TimeSpan.FromSeconds(5);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddProblemDetails();
builder.Services.AddControllers().AddValidationMessages().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    // Reject attempts to submit server-owned fields (userId/status/technicianId/photos).
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
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Service Request API", Version = "v1" });
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
    throw new InvalidOperationException("Configure Identity JWT settings in appsettings.Local.json/environment variables. Run scripts/Initialize-ServiceRequestLocalConfig.ps1 first.");
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("v1/swagger.json", "Service Request API v1"));
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.UseCors(); app.UseAuthentication(); app.UseAuthorization();
app.MapControllers();
app.Run();
public partial class Program;
