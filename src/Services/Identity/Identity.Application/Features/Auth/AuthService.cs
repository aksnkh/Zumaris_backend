using Identity.Application.Abstractions.Persistence;
using Identity.Application.Abstractions.Security;
using Identity.Application.Features.Auth.Models;
using Identity.Domain.Entities;

namespace Identity.Application.Features.Auth;

public sealed class AuthService(
    ISessionRepository sessions,
    IPasswordHasher passwords,
    ITokenService tokens,
    IUserSessionIssuer issuer,
    ApplicationLoginAccess access,
    IApplicationAccountRepository accounts) : IAuthService
{
    private static UnauthorizedAccessException InvalidToken() => new("The session is invalid or expired. Please sign in again.");

    public async Task<(AuthResponse Response, string RefreshToken)> LoginAsync(
        LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            throw new UnauthorizedAccessException("Invalid email or password.");
        if(request.Application is not ("App" or "Tech" or "Admin"))throw new ApplicationLoginException(400,"Application must be App, Tech or Admin.");
        var user = await accounts.FindAsync(request.Application,request.Email,cancellationToken);
        if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(user.PasswordHash) || !passwords.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");

        var grant = await access.ResolveAsync(user.Id, request.Application, cancellationToken);
        return await issuer.IssueAsync(user, cancellationToken, grant.Application, grant.ProviderId);
    }

    public async Task<(AuthResponse Response, string RefreshToken)> RefreshAsync(
        string refreshToken, CancellationToken cancellationToken = default, string? application = null)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 128) throw InvalidToken();
        var oldHash = tokens.HashRefreshToken(refreshToken);
        var session = await sessions.FindByTokenAsync(oldHash, cancellationToken);
        if (session is null || session.RevokedAtUtc.HasValue || session.ExpiresAtUtc <= DateTime.UtcNow)
            throw InvalidToken();

        if (application is not null && application != session.Application)
            throw new ApplicationLoginException(403, "The refresh token belongs to another application.");
        // Re-read the real user instead of trusting stale/missing data in a token record.
        var user = session.Application is "App" or "Tech" or "Admin" ? await accounts.FindByIdAsync(session.Application,session.UserId,cancellationToken) : null;
        if (user is null || !user.IsActive || user.Id != session.UserId)
        {
            await sessions.RevokeAsync(session.Id, session.UserId, cancellationToken);
            throw InvalidToken();
        }

        if (session.Application is not ("App" or "Tech" or "Admin"))
        {
            await sessions.RevokeAsync(session.Id, session.UserId, cancellationToken);
            throw InvalidToken();
        }
        ApplicationGrant grant;
        try { grant = await access.ResolveAsync(user.Id, session.Application, cancellationToken); }
        catch (ApplicationLoginException ex) when (ex.Status == 403)
        {
            await sessions.RevokeAsync(session.Id, session.UserId, cancellationToken);
            throw;
        }
        if (grant.ProviderId != session.ProviderId)
        {
            await sessions.RevokeAsync(session.Id, session.UserId, cancellationToken);
            throw InvalidToken();
        }
        var newRefreshToken = tokens.GenerateRefreshToken();
        var accessToken = tokens.CreateAccessToken(user, session.Id, session.ExpiresAtUtc, grant.Application, grant.ProviderId);
        if (!await sessions.RotateAsync(session.Id, oldHash, tokens.HashRefreshToken(newRefreshToken), cancellationToken))
            throw InvalidToken();
        return (new AuthResponse(accessToken.Token, accessToken.ExpiresAtUtc, user.UserName)
            { UserId = user.Id, RefreshTokenExpiresAtUtc = session.ExpiresAtUtc, Application = grant.Application, Role = grant.Application == "Tech" ? "Provider" : grant.Application == "Admin" ? "Admin" : "Customer", ProviderId = grant.ProviderId }, newRefreshToken);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default, string? application = null)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 128) return;
        var session = await sessions.FindByTokenAsync(tokens.HashRefreshToken(refreshToken), cancellationToken);
        if (session is not null && application is not null && session.Application != application)
            throw new ApplicationLoginException(403, "The refresh token belongs to another application.");
        if (session is not null) await sessions.RevokeAsync(session.Id, session.UserId, cancellationToken);
    }

}
