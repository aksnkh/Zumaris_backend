using Identity.Application.Features.Auth.Models;

namespace Identity.Application.Features.Auth;

public interface IAuthService
{
    Task<(AuthResponse Response, string RefreshToken)> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);

    Task<(AuthResponse Response, string RefreshToken)> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default, string? application = null);

    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default, string? application = null);

}
