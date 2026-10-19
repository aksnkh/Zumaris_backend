using Identity.Application.Abstractions.Persistence;
using Identity.Application.Abstractions.Security;
using Identity.Application.Features.Auth.Models;
using Identity.Domain.Entities;
using Zumaris.Identity.Application.DTOs;

namespace Identity.Application.Features.Auth;

public sealed class AuthService(
    IAuthRepository users,
    ISessionRepository sessions,
    IPasswordHasher passwords,
    ITokenService tokens,
    IUserSessionIssuer issuer) : IAuthService
{
    private static UnauthorizedAccessException InvalidToken() => new("نشست نامعتبر یا منقضی شده است؛ دوباره وارد شوید.");

    public async Task<(AuthResponse Response, string RefreshToken)> LoginAsync(
        LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            throw new UnauthorizedAccessException("نام کاربری یا کلمه عبور نادرست است.");
        var user = await users.GetUserByUsernameAsync(request.UserName.Trim(), cancellationToken);
        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash) || !passwords.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("نام کاربری یا کلمه عبور نادرست است.");

        return await issuer.IssueAsync(user, cancellationToken);
    }

    public async Task<(AuthResponse Response, string RefreshToken)> RefreshAsync(
        string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 128) throw InvalidToken();
        var oldHash = tokens.HashRefreshToken(refreshToken);
        var session = await sessions.FindByTokenAsync(oldHash, cancellationToken);
        if (session is null || session.RevokedAtUtc.HasValue || session.ExpiresAtUtc <= DateTime.UtcNow)
            throw InvalidToken();

        // Re-read the real user instead of trusting stale/missing data in a token record.
        var user = await users.GetUserByUsernameAsync(session.UserName, cancellationToken);
        if (user is null || !user.IsActive || user.Id != session.UserId)
        {
            await sessions.RevokeAsync(session.Id, session.UserId, cancellationToken);
            throw InvalidToken();
        }

        var newRefreshToken = tokens.GenerateRefreshToken();
        var accessToken = tokens.CreateAccessToken(user, session.Id, session.ExpiresAtUtc);
        if (!await sessions.RotateAsync(session.Id, oldHash, tokens.HashRefreshToken(newRefreshToken), cancellationToken))
            throw InvalidToken();
        return (new AuthResponse(accessToken.Token, accessToken.ExpiresAtUtc, user.UserName)
            { UserId = user.Id, RefreshTokenExpiresAtUtc = session.ExpiresAtUtc }, newRefreshToken);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 128) return;
        var session = await sessions.FindByTokenAsync(tokens.HashRefreshToken(refreshToken), cancellationToken);
        if (session is not null) await sessions.RevokeAsync(session.Id, session.UserId, cancellationToken);
    }

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request)
    {
        var passwordHash = passwords.Hash(request.Password);
        try
        {
            await users.RegisterAsync(request.Username, passwordHash, request.Email);
            return new RegisterResponse(true, "User registered successfully.");
        }
        catch (Exception ex) when (ex.Message.Contains("Username already exists", StringComparison.OrdinalIgnoreCase))
        {
            return new RegisterResponse(false, "Username is already taken.");
        }
    }
}
