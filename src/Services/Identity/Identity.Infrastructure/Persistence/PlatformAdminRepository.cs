using Identity.Application.Abstractions.Persistence;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
namespace Identity.Infrastructure.Persistence;
public sealed class PlatformAdminRepository(IConfiguration configuration):IPlatformAdminRepository
{
 public async Task<bool> IsAdminAsync(Guid userId,CancellationToken ct)
 {
  await using var c=new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));await c.OpenAsync(ct);
  await using var cmd=new MySqlCommand("SELECT EXISTS(SELECT 1 FROM admin_accounts WHERE id=@id AND is_active=1 AND is_deleted=0)",c);cmd.Parameters.AddWithValue("@id",userId.ToString());return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
 }
}
