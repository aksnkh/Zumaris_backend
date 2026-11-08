using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Provider.Application;
namespace Zumaris.Provider.Infrastructure.Persistence;
public sealed class ProviderLoginAccessRepository(IConfiguration configuration) : IProviderLoginAccessRepository
{
    public async Task<ProviderLoginAccess?> GetAsync(Guid userId, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand("""
            SELECT p.id,p.user_id,p.is_active,EXISTS(SELECT 1 FROM provider_signup_details s WHERE s.provider_id=p.id) AS signed_up
            FROM providers p WHERE p.user_id=@user
            """, connection);
        command.Parameters.AddWithValue("@user", userId.ToString());
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(1), reader.GetGuid(0), reader.GetBoolean(2), reader.GetBoolean(3)) : null;
    }
}
