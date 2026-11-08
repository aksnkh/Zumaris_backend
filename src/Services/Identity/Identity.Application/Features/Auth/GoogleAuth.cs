using Identity.Application.Abstractions.Persistence;
using Identity.Application.Abstractions.Security;
using Identity.Application.Features.Auth.Models;
using Identity.Domain.Entities;
using Zumaris.Identity.Domain.Entities;

namespace Identity.Application.Features.Auth;

public sealed record VerifiedGoogleIdentity(string Subject, string Email);
public interface IGoogleIdentityValidator
{
    Task<VerifiedGoogleIdentity> ValidateAsync(string idToken, CancellationToken cancellationToken);
}
public interface IExternalLoginRepository
{
    Task<User> FindOrCreateGoogleAsync(VerifiedGoogleIdentity identity, CancellationToken cancellationToken);
    Task LinkGoogleAsync(Guid userId, VerifiedGoogleIdentity identity, CancellationToken cancellationToken);
}
public sealed class ExternalLoginConflictException() : Exception("An account already exists. Sign in to the existing account and explicitly link Google.");
public sealed class GoogleLoginUnavailableException() : Exception("Google sign-in is not configured or temporarily unavailable.");

public interface IUserSessionIssuer
{
    Task<(AuthResponse Response, string RefreshToken)> IssueAsync(User user, CancellationToken cancellationToken, string application = "App", Guid? providerId = null);
}
public sealed class UserSessionIssuer(ISessionRepository sessions, ITokenService tokens, JwtSettings settings) : IUserSessionIssuer
{
    public async Task<(AuthResponse Response, string RefreshToken)> IssueAsync(User user, CancellationToken cancellationToken, string application = "App", Guid? providerId = null)
    {
        if (!user.IsActive) throw new UnauthorizedAccessException("The account is unavailable.");
        var now = DateTime.UtcNow;
        var session = new AuthSession(Guid.NewGuid(), user.Id, user.UserName, now, now.AddDays(settings.RefreshTokenExpirationDays), null) { Application = application, ProviderId = providerId };
        var refresh = tokens.GenerateRefreshToken();
        var access = tokens.CreateAccessToken(user, session.Id, session.ExpiresAtUtc, application, providerId);
        await sessions.CreateAsync(session, tokens.HashRefreshToken(refresh), cancellationToken);
        return (new AuthResponse(access.Token, access.ExpiresAtUtc, user.UserName)
            { UserId = user.Id, RefreshTokenExpiresAtUtc = session.ExpiresAtUtc, Application = application, Role = application == "Tech" ? "Provider" : application == "Admin" ? "Admin" : "Customer", ProviderId = providerId }, refresh);
    }
}

public sealed class GoogleAuthService(IGoogleIdentityValidator validator, IExternalLoginRepository users, IUserSessionIssuer issuer, ApplicationLoginAccess access)
{
    public async Task<(AuthResponse Response, string RefreshToken)> LoginAsync(string idToken, CancellationToken ct, string application = "App")
    {
        var identity = await validator.ValidateAsync(idToken, ct);
        var user = await users.FindOrCreateGoogleAsync(identity, ct);
        var grant = await access.ResolveAsync(user.Id, application, ct);
        return await issuer.IssueAsync(user, ct, grant.Application, grant.ProviderId);
    }
    public async Task LinkAsync(Guid userId, string idToken, CancellationToken ct)
    {
        var identity = await validator.ValidateAsync(idToken, ct);
        await users.LinkGoogleAsync(userId, identity, ct);
    }
}
