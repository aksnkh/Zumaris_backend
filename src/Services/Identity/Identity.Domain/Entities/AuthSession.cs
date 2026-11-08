namespace Identity.Domain.Entities;

public sealed record AuthSession(Guid Id, Guid UserId, string UserName,
    DateTime CreatedAtUtc, DateTime ExpiresAtUtc, DateTime? RevokedAtUtc)
{
    public string? Application { get; init; }
    public Guid? ProviderId { get; init; }
}
