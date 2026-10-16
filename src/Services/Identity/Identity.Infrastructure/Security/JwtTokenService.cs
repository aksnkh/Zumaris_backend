using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Identity.Application.Abstractions.Security;
using Microsoft.IdentityModel.Tokens;
using Zumaris.Identity.Domain.Entities;

namespace Identity.Infrastructure.Security;

public sealed class JwtTokenService(JwtSettings settings) : ITokenService
{
    public IssuedAccessToken CreateAccessToken(User user, Guid sessionId, DateTime sessionExpiresAtUtc)
    {
        var now = DateTime.UtcNow;
        var expires = new[] { now.AddMinutes(settings.AccessTokenExpirationMinutes), sessionExpiresAtUtc }.Min();
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("sid", sessionId.ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(now).ToString(), ClaimValueTypes.Integer64)
        };
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims, now, expires,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)), SecurityAlgorithms.HmacSha256));
        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), token.ValidTo);
    }

    public string GenerateRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));

    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken))).ToLowerInvariant();
}
