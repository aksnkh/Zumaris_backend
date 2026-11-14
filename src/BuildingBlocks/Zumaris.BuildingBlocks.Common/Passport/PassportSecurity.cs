using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Zumaris.BuildingBlocks.Common.Security;
namespace Zumaris.BuildingBlocks.Common.Passport;
public static class PassportSecurity
{
    public static void AddPassportSecurity(this WebApplicationBuilder b)
    {
        b.Configuration.AddJsonFile("appsettings.Local.json", true, false).AddEnvironmentVariables().AddCommandLine(Environment.GetCommandLineArgs().Skip(1).ToArray());
        var key = b.Configuration["Jwt:Key"] ?? ""; var issuer = b.Configuration["Jwt:Issuer"]; var audience = b.Configuration["Jwt:Audience"];
        if (Encoding.UTF8.GetByteCount(key) < 32 || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience)) throw new InvalidOperationException("Configure the Identity JWT key, issuer and audience.");
        var identity = new Uri(b.Configuration["Identity:BaseUrl"] ?? "http://127.0.0.1:5001/");
        if (!identity.IsAbsoluteUri || !string.IsNullOrEmpty(identity.UserInfo) || !identity.AbsoluteUri.EndsWith('/') || !(identity.Scheme == "https" || identity.Scheme == "http" && identity.IsLoopback)) throw new InvalidOperationException("Identity:BaseUrl must use HTTPS or loopback HTTP with a trailing slash.");
        b.Services.AddApplicationIsolation("App"); b.Services.AddHttpContextAccessor();
        b.Services.AddHttpClient("passport", c => c.Timeout = TimeSpan.FromSeconds(10)).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        b.Services.AddScoped<PassportHttpClient>(); b.Services.AddScoped<PassportExceptionFilter>();
        b.Services.Configure<ForwardedHeadersOptions>(o => { o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto; o.ForwardLimit = 1; o.KnownNetworks.Clear(); o.KnownProxies.Clear(); o.KnownProxies.Add(IPAddress.Loopback); o.KnownProxies.Add(IPAddress.IPv6Loopback); });
        b.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false; o.IncludeErrorDetails = false;
            o.TokenValidationParameters = new() { ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true, RequireSignedTokens = true, RequireExpirationTime = true, ValidIssuer = issuer, ValidAudience = audience, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ValidTypes = ["JWT"], ClockSkew = TimeSpan.FromSeconds(15) };
            o.Events = new()
            {
                OnTokenValidated = async c =>
                {
                    if (!Guid.TryParse(c.Principal?.FindFirst("sub")?.Value, out var id) || id == Guid.Empty || !Guid.TryParse(c.Principal?.FindFirst("sid")?.Value, out var sid) || sid == Guid.Empty) { c.Fail("Invalid identity claims."); return; }
                    using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(identity, "api/auth/session")); req.Headers.TryAddWithoutValidation("Authorization", c.Request.Headers.Authorization.ToString()); if (identity.IsLoopback) req.Headers.Add("X-Forwarded-Proto", "https");
                    try { using var r = await c.HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("passport").SendAsync(req, c.HttpContext.RequestAborted); if (r.StatusCode == HttpStatusCode.NoContent) return; if (r.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)) c.HttpContext.Items["IdentityUnavailable"] = true; c.Fail("Session is not active."); }
                    catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { c.HttpContext.Items["IdentityUnavailable"] = true; c.Fail("Identity unavailable."); }
                },
                OnChallenge = async c => { c.HandleResponse(); int status = c.HttpContext.Items.ContainsKey("IdentityUnavailable") ? 503 : 401; c.Response.StatusCode = status; await c.Response.WriteAsJsonAsync(PassportResponse.Of<object?>(null, status == 401 ? "Authentication is required." : "Identity session validation is temporarily unavailable.", status), PassportResponse.Json); },
                OnForbidden = async c => { c.Response.StatusCode = 403; await c.Response.WriteAsJsonAsync(PassportResponse.Of<object?>(null, "Access denied.", 403), PassportResponse.Json); }
            };
        }); b.Services.AddAuthorization();
    }
}
