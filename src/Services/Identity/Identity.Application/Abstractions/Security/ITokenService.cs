using Identity.Domain.Entities;
using Zumaris.Identity.Domain.Entities;

namespace Identity.Application.Abstractions.Security;

public interface ITokenService
{
    IssuedAccessToken CreateAccessToken(User user, Guid sessionId, DateTime sessionExpiresAtUtc);
    string GenerateRefreshToken();
    string HashRefreshToken(string refreshToken);
}

public sealed record IssuedAccessToken(string Token, DateTime ExpiresAtUtc);


