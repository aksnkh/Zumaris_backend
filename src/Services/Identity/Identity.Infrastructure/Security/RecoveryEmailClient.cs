using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Identity.Application.Abstractions.Security;
using Identity.Application.Features.Auth;
using Microsoft.IdentityModel.Tokens;
namespace Identity.Infrastructure.Security;
public sealed class RecoveryEmailClient(HttpClient client, JwtSettings settings) : IRecoveryEmailClient
{
    public async Task Send(RecoveryEmail email, CancellationToken ct)
    {
        var now = DateTime.UtcNow; var jwt = new JwtSecurityToken(settings.Issuer, "Zumaris.Notification.PasswordRecovery",
         [new("sub", "identity-password-recovery"), new("scope", "password-reset-email"), new("application", email.Application), new("delivery_id", email.DeliveryId.ToString()), new("email_hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Recipient)))), new("jti", Guid.NewGuid().ToString())], now, now.AddSeconds(30), new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)), SecurityAlgorithms.HmacSha256));
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/internal/password-recovery-email"); request.Headers.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt)); request.Content = JsonContent.Create(email);
        using var response = await client.SendAsync(request, ct); if (response.StatusCode != System.Net.HttpStatusCode.NoContent) throw new HttpRequestException("Recovery email delivery is unavailable.");
    }
}
