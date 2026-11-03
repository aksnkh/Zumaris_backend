using Identity.Application.Abstractions.Persistence;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
namespace Identity.Infrastructure.Persistence;
public sealed class PlatformAdminRepository(IConfiguration configuration) : IPlatformAdminRepository
{
    public async Task<bool> IsAdminAsync(Guid userId, CancellationToken ct)
    {
        await using var c = new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await c.OpenAsync(ct);
        await using var cmd = new MySqlCommand("SELECT EXISTS(SELECT 1 FROM platform_administrators WHERE user_id=@user AND is_active=1)", c);
        cmd.Parameters.AddWithValue("@user", userId.ToString());
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }
}
