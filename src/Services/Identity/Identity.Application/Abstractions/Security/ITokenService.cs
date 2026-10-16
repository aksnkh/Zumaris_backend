using Identity.Domain.Entities;
using Zumaris.Identity.Domain.Entities;

namespace Identity.Application.Abstractions.Security;

public interface ITokenService
{
    string CreateAccessToken(User user);
    string GenerateRefreshToken();
    string HashRefreshToken(string refreshToken);
}


