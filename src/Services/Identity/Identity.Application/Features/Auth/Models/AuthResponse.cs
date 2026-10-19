namespace Identity.Application.Features.Auth.Models;

public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string UserName
)
{
    public Guid UserId { get; init; }
    public DateTime RefreshTokenExpiresAtUtc { get; init; }
}
