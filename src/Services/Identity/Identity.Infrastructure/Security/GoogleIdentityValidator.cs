using Google.Apis.Auth;
using Identity.Application.Features.Auth;
using Microsoft.Extensions.Configuration;

namespace Identity.Infrastructure.Security;

public sealed class GoogleIdentityValidator(IConfiguration configuration) : IGoogleIdentityValidator
{
    public async Task<VerifiedGoogleIdentity> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        var clients = configuration.GetSection("Google:ClientIds").Get<string[]>() ?? [];
        if (clients.Length == 0 || clients.Any(c => string.IsNullOrWhiteSpace(c) || c.Contains('*')))
            throw new GoogleLoginUnavailableException();
        if (string.IsNullOrWhiteSpace(idToken) || idToken.Length > 16_384) throw new UnauthorizedAccessException("Invalid Google identity.");
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = clients,
                IssuedAtClockTolerance = TimeSpan.FromSeconds(30),
                ExpirationTimeClockTolerance = TimeSpan.FromSeconds(30)
            }).WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
            // The official verifier validates signature, audience and timestamps; require
            // Google's issuer, stable subject and a verified email explicitly too.
            if (payload.Issuer is not ("accounts.google.com" or "https://accounts.google.com") ||
                string.IsNullOrWhiteSpace(payload.Subject) || payload.Subject.Length > 255 || !payload.EmailVerified ||
                string.IsNullOrWhiteSpace(payload.Email) || payload.Email.Length > 150 ||
                !System.Net.Mail.MailAddress.TryCreate(payload.Email, out var email) || email.Address != payload.Email)
                throw new UnauthorizedAccessException("Invalid Google identity.");
            return new VerifiedGoogleIdentity(payload.Subject, payload.Email.Trim().ToLowerInvariant());
        }
        catch (InvalidJwtException) { throw new UnauthorizedAccessException("Invalid Google identity."); }
        catch (HttpRequestException) { throw new GoogleLoginUnavailableException(); }
        catch (TimeoutException) { throw new GoogleLoginUnavailableException(); }
    }
}
