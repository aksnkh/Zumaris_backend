using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Provider.Application;
namespace Zumaris.Provider.Infrastructure.Persistence;
public sealed class ProviderQuoteRepository(IConfiguration configuration):IProviderQuoteRepository
{
    public async Task<ProviderQuoteProfile?> GetAsync(Guid? userId,Guid? providerId,int categoryId,CancellationToken ct)
    {
        var cs=configuration.GetConnectionString("DefaultConnection")??throw new InvalidOperationException("DefaultConnection is required.");
        await using var c=new MySqlConnection(cs);await c.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT p.* FROM providers p JOIN provider_category_qualifications q ON q.provider_id=p.id
            WHERE p.is_active=1 AND p.is_verified=1 AND q.is_qualified=1 AND q.category_id=@category
            AND ((@user IS NOT NULL AND p.user_id=@user) OR (@provider IS NOT NULL AND p.id=@provider))
            """,c);
        cmd.Parameters.AddWithValue("@user",userId?.ToString());cmd.Parameters.AddWithValue("@provider",providerId?.ToString());cmd.Parameters.AddWithValue("@category",categoryId);
        await using var reader=await cmd.ExecuteReaderAsync(ct);if(!await reader.ReadAsync(ct))return null;
        return new(reader.GetGuid(reader.GetOrdinal("id")),reader.GetGuid(reader.GetOrdinal("user_id")),reader.GetString("display_name"),
            reader.IsDBNull(reader.GetOrdinal("photo_url"))?null:reader.GetString("photo_url"),
            reader.IsDBNull(reader.GetOrdinal("rating"))?null:reader.GetDecimal("rating"),
            reader.IsDBNull(reader.GetOrdinal("completed_jobs"))?null:reader.GetInt32("completed_jobs"),true,categoryId);
    }
}
