using Identity.Application.Abstractions.Persistence;
using Identity.Application.Abstractions.Security;
using Identity.Application.Features.Auth.Models;
using Identity.Domain.Entities;
using Microsoft.Extensions.Configuration;
using System.Data;
using Zumaris.Identity.Application.DTOs;
using Zumaris.Identity.Domain.Entities;

namespace Identity.Application.Features.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IAuthRepository _authRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;

    public AuthService(
        IAuthRepository authRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IConfiguration configuration)
    {
        _authRepository = authRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _configuration = configuration;
    }

    public async Task<(AuthResponse Response, string RefreshToken)> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _authRepository.GetUserByUsernameAsync(request.UserName.Trim(), cancellationToken);

        if (user is null || !user.IsActive)
            throw new UnauthorizedAccessException("نام کاربری یا کلمه عبور نادرست است.");


        Console.WriteLine($"Input Password: '{request.Password}'");
        Console.WriteLine($"DB PasswordHash: '{user.PasswordHash}'");
        Console.WriteLine($"Verify Result: {_passwordHasher.Verify(request.Password, user.PasswordHash)}");

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("نام کاربری یا کلمه عبور نادرست است.");

        var rawRefreshToken = _tokenService.GenerateRefreshToken();
        var hashedRefreshToken = _tokenService.HashRefreshToken(rawRefreshToken);

        var refreshDays = _configuration.GetValue<int>("Jwt:RefreshTokenExpirationDays", 7);
        var accessMinutes = _configuration.GetValue<int>("Jwt:AccessTokenExpirationMinutes", 15);

        var expiresAtUtc = DateTime.UtcNow.AddDays(refreshDays);
        var createdAtUtc = DateTime.UtcNow;

        await _authRepository.InsertRefreshTokenAsync(
            Guid.NewGuid(),
            user.Id,
            hashedRefreshToken,
            expiresAtUtc,
            createdAtUtc,
            cancellationToken);

        var accessToken = _tokenService.CreateAccessToken(user);

        return (
            new AuthResponse(accessToken, DateTime.UtcNow.AddMinutes(accessMinutes), user.UserName),
            rawRefreshToken
        );
    }

    public async Task<(AuthResponse Response, string RefreshToken)> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new UnauthorizedAccessException("Refresh token ارسال نشده است.");

        var hashedToken = _tokenService.HashRefreshToken(refreshToken);
        var storedRecord = await _authRepository.GetRefreshTokenAsync(hashedToken, cancellationToken);

        if (storedRecord is null || !storedRecord.IsActive)
            throw new UnauthorizedAccessException("Refresh token نامعتبر یا منقضی شده است.");

        var newRawRefreshToken = _tokenService.GenerateRefreshToken();
        var newHashedRefreshToken = _tokenService.HashRefreshToken(newRawRefreshToken);

        var refreshDays = _configuration.GetValue<int>("Jwt:RefreshTokenExpirationDays", 7);
        var accessMinutes = _configuration.GetValue<int>("Jwt:AccessTokenExpirationMinutes", 15);

        var newExpiresAtUtc = DateTime.UtcNow.AddDays(refreshDays);
        var newCreatedAtUtc = DateTime.UtcNow;

        var isRotated = await _authRepository.RotateRefreshTokenAsync(
            hashedToken,
            Guid.NewGuid(),
            storedRecord.UserId,
            newHashedRefreshToken,
            newExpiresAtUtc,
            newCreatedAtUtc,
            cancellationToken);

        if (!isRotated)
            throw new UnauthorizedAccessException("خطا در چرخش توکن. لطفاً مجدداً لاگین کنید.");

        var user = new User
        {
            Id = storedRecord.UserId,
            UserName = storedRecord.UserName,
            Email = storedRecord.Email,
            IsActive = storedRecord.UserIsActive            
        };

        var newAccessToken = _tokenService.CreateAccessToken(user);

        return (
            new AuthResponse(newAccessToken, DateTime.UtcNow.AddMinutes(accessMinutes), user.UserName),
            newRawRefreshToken
        );
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return;

        var hashedToken = _tokenService.HashRefreshToken(refreshToken);
        await _authRepository.RevokeRefreshTokenAsync(hashedToken, cancellationToken);
    }

    // در لایه Application
    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request)
    {
        string passwordHash = _passwordHasher.Hash(request.Password);

        try
        {
            await _authRepository.RegisterAsync(
                request.Username,
                passwordHash,
                request.Email
            );

            return new RegisterResponse(true, "User registered successfully.");
        }
        catch (Exception ex) when (ex.Message.Contains("Username already exists"))
        {
            return new RegisterResponse(false, "Username is already taken.");
        }
        catch (Exception ex)
        {
            return new RegisterResponse(false, ex.Message);
        }
    }

}
