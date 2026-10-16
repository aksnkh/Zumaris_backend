using System.Text;

namespace Identity.Application.Abstractions.Security;

public sealed class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 7;

    public void Validate()
    {
        if (Encoding.UTF8.GetByteCount(Key) < 32 || string.IsNullOrWhiteSpace(Issuer) ||
            string.IsNullOrWhiteSpace(Audience))
            throw new InvalidOperationException("Configure Jwt:Key (at least 32 UTF-8 bytes), Jwt:Issuer and Jwt:Audience.");
        if (AccessTokenExpirationMinutes is < 1 or > 30 || RefreshTokenExpirationDays is < 1 or > 30)
            throw new InvalidOperationException("JWT access lifetime must be 1–30 minutes; session lifetime must be 1–30 days.");
    }
}
