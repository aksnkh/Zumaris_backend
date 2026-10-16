using Identity.Application.Features.Auth.Models;
using Zumaris.Identity.Application.DTOs;

namespace Identity.Application.Features.Auth;

public interface IAuthService
{
    Task<(AuthResponse Response, string RefreshToken)> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);

    Task<(AuthResponse Response, string RefreshToken)> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<RegisterResponse> RegisterAsync(RegisterRequest request);
}
