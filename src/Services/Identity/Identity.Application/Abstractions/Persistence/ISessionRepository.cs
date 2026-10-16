using Identity.Domain.Entities;

namespace Identity.Application.Abstractions.Persistence;

public interface ISessionRepository
{
    Task CreateAsync(AuthSession session, string tokenHash, CancellationToken cancellationToken = default);
    Task<AuthSession?> FindByTokenAsync(string tokenHash, CancellationToken cancellationToken = default);
    // Consumes the old token exactly once. Reuse revokes the whole session atomically.
    Task<bool> RotateAsync(Guid sessionId, string oldHash, string newHash, CancellationToken cancellationToken = default);
    Task<bool> IsActiveAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuthSession>> ListAsync(Guid userId, CancellationToken cancellationToken = default);
    Task RevokeAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);
    Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default);
}
