using System.Net;
using System.Net.Http.Headers;
using Zumaris.ServiceCatalog.Application.Abstractions;

namespace Zumaris.ServiceCatalog.Infrastructure.Integrations;

public sealed class IdentitySessionValidator(HttpClient client) : IIdentitySessionValidator
{
    public async Task<bool> IsActiveAsync(string accessToken, bool originalRequestIsHttps, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/session");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        // Preserve HTTPS when both services communicate over trusted loopback HTTP on IIS.
        if (originalRequestIsHttps) request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return false;
        // Exactly 204 is the contract. A redirect, rate limit, stale deployment (404),
        // or unavailable Identity must fail closed, rather than accepting the token.
        if (response.StatusCode != HttpStatusCode.NoContent)
            throw new HttpRequestException("Identity session validation is unavailable.");
        return true;
    }
}
