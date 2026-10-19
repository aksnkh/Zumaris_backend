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
    Task<(AuthResponse Response, string RefreshToken)> IssueAsync(User user, CancellationToken cancellationToken);
}
public sealed class UserSessionIssuer(ISessionRepository sessions, ITokenService tokens, JwtSettings settings) : IUserSessionIssuer
{
    public async Task<(AuthResponse Response, string RefreshToken)> IssueAsync(User user, CancellationToken cancellationToken)
    {
        if (!user.IsActive) throw new UnauthorizedAccessException("The account is unavailable.");
        var now = DateTime.UtcNow;
        var session = new AuthSession(Guid.NewGuid(), user.Id, user.UserName, now, now.AddDays(settings.RefreshTokenExpirationDays), null);
        var refresh = tokens.GenerateRefreshToken();
        var access = tokens.CreateAccessToken(user, session.Id, session.ExpiresAtUtc);
        await sessions.CreateAsync(session, tokens.HashRefreshToken(refresh), cancellationToken);
        return (new AuthResponse(access.Token, access.ExpiresAtUtc, user.UserName)
            { UserId = user.Id, RefreshTokenExpiresAtUtc = session.ExpiresAtUtc }, refresh);
    }
}

public sealed class GoogleAuthService(IGoogleIdentityValidator validator, IExternalLoginRepository users, IUserSessionIssuer issuer)
{
    public async Task<(AuthResponse Response, string RefreshToken)> LoginAsync(string idToken, CancellationToken ct)
    {
        var identity = await validator.ValidateAsync(idToken, ct);
        var user = await users.FindOrCreateGoogleAsync(identity, ct);
        return await issuer.IssueAsync(user, ct);
    }
    public async Task LinkAsync(Guid userId, string idToken, CancellationToken ct)
    {
        var identity = await validator.ValidateAsync(idToken, ct);
        await users.LinkGoogleAsync(userId, identity, ct);
    }
}
