using System.Data;
using Identity.Application.Features.Profile;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
namespace Identity.Infrastructure.Persistence;

public sealed class ProfileAdoNetRepository(IConfiguration configuration) : IProfileRepository
{
    private readonly string connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection is required.");
    private const string Select = """
        SELECT u.id, u.user_name, u.email, p.first_name, p.last_name, p.phone, p.photo_path,
               p.updated_at
        FROM identity_users u LEFT JOIN identity_user_profiles p ON p.user_id=u.id
        WHERE u.id=@user AND u.is_active=1 AND u.is_deleted=0
        """;
    private static string? Text(MySqlDataReader reader,string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(name);
    private static async Task<ProfileRecord?> ReadAsync(MySqlConnection c, MySqlTransaction? tx, Guid user, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(Select,c,tx);
        cmd.Parameters.AddWithValue("@user",user.ToString());
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow,ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(reader.GetGuid(reader.GetOrdinal("id")),reader.GetString("user_name"),reader.GetString("email"),
            Text(reader,"first_name"),Text(reader,"last_name"),Text(reader,"phone"),Text(reader,"photo_path"),
            reader.IsDBNull(reader.GetOrdinal("updated_at")) ? null : DateTime.SpecifyKind(reader.GetDateTime("updated_at"),DateTimeKind.Utc));
    }
    public async Task<ProfileRecord?> GetAsync(Guid userId, CancellationToken ct)
    {
        await using var c = new MySqlConnection(connectionString); await c.OpenAsync(ct);
        return await ReadAsync(c,null,userId,ct);
    }
    private async Task<ProfileWrite> WriteAsync(Guid userId,UpdateProfileRequest? details,bool photoChange,string? photoPath,DateTime now,CancellationToken ct)
    {
        await using var c = new MySqlConnection(connectionString); await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);
        // Serialize even the first write to a previously absent profile row.
        await using (var userLock = new MySqlCommand("SELECT id FROM identity_users WHERE id=@user AND is_active=1 AND is_deleted=0 FOR UPDATE",c,tx))
        {
            userLock.Parameters.AddWithValue("@user",userId.ToString());
            if (await userLock.ExecuteScalarAsync(ct) is null) throw new ProfileNotFoundException();
        }
        var current = await ReadAsync(c,tx,userId,ct) ?? throw new ProfileNotFoundException();
        await using (var ensure = new MySqlCommand("INSERT INTO identity_user_profiles (user_id) VALUES (@user) ON DUPLICATE KEY UPDATE user_id=user_id",c,tx))
        {
            ensure.Parameters.AddWithValue("@user",userId.ToString()); await ensure.ExecuteNonQueryAsync(ct);
        }
        var sql = photoChange
            ? "UPDATE identity_user_profiles SET photo_path=@photo,updated_at=@now WHERE user_id=@user"
            : "UPDATE identity_user_profiles SET first_name=@first,last_name=@last,phone=@phone,updated_at=@now WHERE user_id=@user";
        await using (var cmd = new MySqlCommand(sql,c,tx))
        {
            cmd.Parameters.AddWithValue("@user",userId.ToString());
            cmd.Parameters.AddWithValue("@now",now);
            if (photoChange) cmd.Parameters.AddWithValue("@photo",(object?)photoPath ?? DBNull.Value);
            else
            {
                cmd.Parameters.AddWithValue("@first",details!.FirstName); cmd.Parameters.AddWithValue("@last",details.LastName);
                cmd.Parameters.AddWithValue("@phone",(object?)details.Phone ?? DBNull.Value);
            }
            await cmd.ExecuteNonQueryAsync(ct);
        }
        var updated = await ReadAsync(c,tx,userId,ct) ?? throw new ProfileNotFoundException();
        await tx.CommitAsync(ct);
        return new(updated,photoChange ? current.PhotoPath : null);
    }
    public Task<ProfileWrite> UpdateAsync(Guid userId,UpdateProfileRequest request,DateTime now,CancellationToken ct) =>
        WriteAsync(userId,request,false,null,now,ct);
    public Task<ProfileWrite> SetPhotoAsync(Guid userId,string? path,DateTime now,CancellationToken ct) =>
        WriteAsync(userId,null,true,path,now,ct);
}
