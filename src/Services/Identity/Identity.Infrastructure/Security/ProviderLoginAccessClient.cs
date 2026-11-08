using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Identity.Application.Abstractions.Security;
using Identity.Application.Features.Auth;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Security;

public sealed class ProviderLoginAccessClient(HttpClient client, JwtSettings settings) : IProviderLoginAccessClient
{
    private sealed record Envelope(ProviderLoginAccess? Payload);
    public async Task<ProviderLoginAccess?> GetAsync(Guid userId, CancellationToken ct)
    {
        // Purpose-bound, 30-second server credential, never an end-user session token.
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(settings.Issuer, "Zumaris.Provider.LoginAccess",
            [new Claim("sub", userId.ToString()), new Claim("scope", "provider-login-access"),
             new Claim("jti", Guid.NewGuid().ToString())], now, now.AddSeconds(30),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)), SecurityAlgorithms.HmacSha256));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/internal/users/{userId}/login-access");
        request.Headers.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode != HttpStatusCode.OK) throw Unavailable();
            var envelope = await response.Content.ReadFromJsonAsync<Envelope>(cancellationToken: ct);
            return envelope?.Payload ?? throw Unavailable();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or NotSupportedException ||
            ex is OperationCanceledException && !ct.IsCancellationRequested) { throw Unavailable(); }
    }
    private static ApplicationLoginException Unavailable() => new(503, "Provider sign-in validation is temporarily unavailable.");
}
