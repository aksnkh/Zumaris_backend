namespace Identity.Application.Features.Auth.Models;

public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string UserName
)
{
    public string Application { get; init; } = "";
    public string Role { get; init; } = "";
    public Guid? ProviderId { get; init; }
    public Guid UserId { get; init; }
    public DateTime RefreshTokenExpiresAtUtc { get; init; }
}
