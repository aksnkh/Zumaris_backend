using Identity.Application.Features.Administration;
using Zumaris.BuildingBlocks.Common.Security;
using Identity.Application.Features.Signup;
using Identity.Application.Features.Profile;
using Identity.Infrastructure.Storage;
using Zumaris.BuildingBlocks.Common.Validation;
using System.Text;
using Identity.Api.Controllers;
using Identity.Application.Abstractions.Persistence;
using Identity.Application.Abstractions.Security;
using Identity.Application.Features.Auth;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Threading.RateLimiting;

using Identity.Api.Security;
using Microsoft.AspNetCore.DataProtection;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplicationIsolation("App", "Tech", "Admin");
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables().AddCommandLine(args);
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
jwt.Validate();
builder.Services.AddSingleton(jwt);
builder.Services.AddProblemDetails();
// IIS Gateway and Identity share this machine. Trust forwarding only from loopback,
// so HTTPS terminated by Gateway is preserved without accepting arbitrary client headers.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(System.Net.IPAddress.Loopback);
    options.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
});
builder.Services.AddControllers().AddValidationMessages();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Identity API", Version = "v1" });
    // Relative to the JSON document: works directly and through /identity on Gateway.
    c.AddServer(new OpenApiServer { Url = "../.." });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    c.AddSecurityDefinition("CSRF", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-Zumaris-CSRF",
        Description = "Enter 1. Required for POST, PUT and DELETE requests."
    });
    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>(),
        [new OpenApiSecuritySchemeReference("CSRF", document)] = new List<string>()
    });
});

builder.Services.AddScoped<ISessionRepository, SessionAdoNetRepository>();
builder.Services.AddScoped<IPlatformAdminRepository, PlatformAdminRepository>();
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IApplicationAccountRepository, ApplicationAccountRepository>();
builder.Services.AddScoped<IndependentRegistrationService>();
builder.Services.AddScoped<ApplicationLoginAccess>();
var providerUri = new Uri(builder.Configuration["Provider:BaseUrl"] ?? "http://127.0.0.1:5007/");
if (!providerUri.IsAbsoluteUri || !string.IsNullOrEmpty(providerUri.UserInfo) || !providerUri.AbsoluteUri.EndsWith('/') ||
    !(providerUri.Scheme == "https" || providerUri.Scheme == "http" && providerUri.IsLoopback))
    throw new InvalidOperationException("Provider:BaseUrl must use HTTPS or loopback HTTP, end with a slash and contain no credentials.");
builder.Services.AddHttpClient<IProviderLoginAccessClient, ProviderLoginAccessClient>(client =>
{
    client.BaseAddress = providerUri;
    client.Timeout = TimeSpan.FromSeconds(5);
    if (providerUri.IsLoopback) client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<IProviderEnrollmentClient,ProviderEnrollmentClient>(client=>
{
 client.BaseAddress=providerUri;client.Timeout=TimeSpan.FromSeconds(5);
 if(providerUri.IsLoopback)client.DefaultRequestHeaders.Add("X-Forwarded-Proto","https");
}).ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false});
builder.Services.AddScoped<AdministratorRegistrationFilter>();
builder.Services.AddScoped<ISignupRepository,SignupRepository>();
builder.Services.AddScoped<SignupService>();
builder.Services.AddScoped<IAdministratorAccountRepository, AdministratorAccountRepository>();
builder.Services.AddScoped<AdministratorAccountService>();
builder.Services.AddScoped<ActiveAdministratorFilter>();
builder.Services.AddScoped<AdministratorBootstrapKeyFilter>();
builder.Services.AddScoped<SignupExceptionFilter>();
builder.Services.AddScoped<IUserSessionIssuer, UserSessionIssuer>();
builder.Services.AddScoped<IGoogleIdentityValidator, GoogleIdentityValidator>();
builder.Services.AddScoped<IExternalLoginRepository, ExternalLoginAdoNetRepository>();
builder.Services.AddScoped<GoogleAuthService>();
builder.Services.AddScoped<BffOnlyFilter>();
builder.Services.AddSingleton(TimeProvider.System);
var recovery = builder.Configuration.GetSection("PasswordRecovery").Get<PasswordRecoverySettings>() ?? new();
recovery.Validate();
builder.Services.AddSingleton(recovery);
var protection = builder.Services.AddDataProtection().SetApplicationName("Zumaris.Identity");
if (recovery.Enabled)
{
    var keyPath = builder.Configuration["DataProtection:KeysPath"];
    if (string.IsNullOrWhiteSpace(keyPath) || !Path.IsPathFullyQualified(keyPath)) throw new InvalidOperationException("Configure a private, persistent DataProtection:KeysPath before enabling recovery.");
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
}
builder.Services.AddScoped<IRecoveryProtector,RecoveryProtection>();
builder.Services.AddScoped<IPasswordRecoveryRepository,PasswordRecoveryRepository>();
builder.Services.AddScoped<PasswordRecoveryService>();
builder.Services.AddScoped<RecoveryExceptionFilter>();
builder.Services.AddHostedService<RecoveryEmailWorker>();
var notificationUri = new Uri(builder.Configuration["Notification:BaseUrl"] ?? "http://127.0.0.1:5009/");
if (!notificationUri.IsAbsoluteUri || !string.IsNullOrEmpty(notificationUri.UserInfo) || !notificationUri.AbsoluteUri.EndsWith('/') || !(notificationUri.Scheme == "https" || notificationUri.Scheme == "http" && notificationUri.IsLoopback)) throw new InvalidOperationException("Notification:BaseUrl must use HTTPS or loopback HTTP with a trailing slash.");
builder.Services.AddHttpClient<IRecoveryEmailClient,RecoveryEmailClient>(c => { c.BaseAddress=notificationUri; c.Timeout=TimeSpan.FromSeconds(20); }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect=false });
builder.Services.AddScoped<IProfileRepository,ProfileAdoNetRepository>();
builder.Services.AddScoped<IProfilePhotoStorage,LocalProfilePhotoStorage>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<ProfileExceptionFilter>();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
}));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        var path=context.HttpContext.Request.Path.Value??"";
        if(path.EndsWith("/forgot-password",StringComparison.OrdinalIgnoreCase)||path.EndsWith("/reset-password",StringComparison.OrdinalIgnoreCase))
            await context.HttpContext.Response.WriteAsJsonAsync(Zumaris.BuildingBlocks.Common.Responses.ApiResponseError.Create(429,"Too many password recovery attempts. Please try again later.",context.HttpContext.TraceIdentifier),new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web){Converters={new System.Text.Json.Serialization.JsonStringEnumConverter<Zumaris.BuildingBlocks.Common.Responses.MessageLevel>()}},ct);
    };
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        (context.Request.Path.Value?.EndsWith("/forgot-password",StringComparison.OrdinalIgnoreCase)==true ? "forgot:" : context.Request.Path.Value?.EndsWith("/reset-password",StringComparison.OrdinalIgnoreCase)==true ? "reset:" : "auth:") + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"), _ => new FixedWindowRateLimiterOptions
        { PermitLimit = context.Request.Path.Value?.EndsWith("/forgot-password",StringComparison.OrdinalIgnoreCase)==true ? 5 : context.Request.Path.Value?.EndsWith("/reset-password",StringComparison.OrdinalIgnoreCase)==true ? 10 : 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.IncludeErrorDetails = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
        ValidateIssuerSigningKey = true, RequireSignedTokens = true, RequireExpirationTime = true,
        ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidTypes = ["JWT"], NameClaimType = "unique_name", ClockSkew = TimeSpan.FromSeconds(15)
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var userId) ||
                !Guid.TryParse(context.Principal?.FindFirst("sid")?.Value, out var sessionId))
            { context.Fail("Invalid session claims."); return; }
            var sessions = context.HttpContext.RequestServices.GetRequiredService<ISessionRepository>();
            var session = await sessions.FindByIdAsync(sessionId, userId, context.HttpContext.RequestAborted);
            if (session is null || !Applications.HasValidContext(context.Principal!) ||
                session.Application != context.Principal!.FindFirst("application")?.Value ||
                session.ProviderId?.ToString() != context.Principal.FindFirst("providerId")?.Value)
                context.Fail("The application session is invalid or expired.");
        },
        OnChallenge = async context =>
        {
            if (!context.Request.Path.Equals("/api/admin/accounts", StringComparison.OrdinalIgnoreCase)) return;
            context.HandleResponse();
            await Results.Json(Zumaris.BuildingBlocks.Common.Responses.ApiResponseError.Create(401,
                "Sign in to Admin to create an administrator account.", context.HttpContext.TraceIdentifier),
                options: new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter<Zumaris.BuildingBlocks.Common.Responses.MessageLevel>() } },
                statusCode: 401).ExecuteAsync(context.HttpContext);
        }
    };
});
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseForwardedHeaders();
// Never return connection details, SQL errors or stack traces to clients (including Development).
app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("v1/swagger.json", "Identity API v1"));
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers().RequireRateLimiting("auth");
app.Run();

// Exposes the entry point to integration tests without changing production registration.
public partial class Program;
