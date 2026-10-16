using Identity.Domain.Entities;
using Zumaris.Identity.Domain.Entities;

namespace Identity.Application.Abstractions.Persistence;

public interface IAuthRepository
{
    Task<User?> GetUserByUsernameAsync(string userName, CancellationToken cancellationToken = default);

    Task InsertRefreshTokenAsync(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTime expiresAtUtc,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default);

    Task<RefreshTokenRecord?> GetRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task<bool> RotateRefreshTokenAsync(
        string oldTokenHash,
        Guid newId,
        Guid userId,
        string newTokenHash,
        DateTime newExpiresAtUtc,
        DateTime newCreatedAtUtc,
        CancellationToken cancellationToken = default);

    Task RevokeRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task<bool> RegisterAsync(string username, string passwordHash, string email);
}
