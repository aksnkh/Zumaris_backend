using System.Data;
using System.Security.Cryptography;
using System.Text;
using Identity.Application.Features.Auth;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Identity.Domain.Entities;

namespace Identity.Infrastructure.Persistence;

public sealed class ExternalLoginAdoNetRepository(IConfiguration configuration) : IExternalLoginRepository
{
    private readonly string connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection is required.");
    private static MySqlCommand Command(MySqlConnection c, MySqlTransaction? tx, string sql, params (string, object)[] parameters)
    {
        var cmd = new MySqlCommand(sql, c, tx);
        foreach (var (key, value) in parameters) cmd.Parameters.AddWithValue(key, value);
        return cmd;
    }
    private static User Read(MySqlDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")), UserName = reader.GetString("user_name"), Email = reader.GetString("email"),
        PasswordHash = reader.IsDBNull(reader.GetOrdinal("password_hash")) ? null : reader.GetString("password_hash"),
        IsActive = reader.GetBoolean("is_active") && !reader.GetBoolean("is_deleted")
    };
    private static async Task<User?> Find(MySqlConnection c, MySqlTransaction? tx, string subject, CancellationToken ct)
    {
        await using var cmd = Command(c, tx, """
            SELECT u.* FROM identity_external_logins e INNER JOIN identity_users u ON u.id=e.user_id
            WHERE e.provider='Google' AND e.provider_subject=@subject
            """, ("@subject", subject));
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }
    public async Task<User> FindOrCreateGoogleAsync(VerifiedGoogleIdentity identity, CancellationToken ct)
    {
        await using var c = new MySqlConnection(connectionString); await c.OpenAsync(ct);
        var existing = await Find(c, null, identity.Subject, ct);
        if (existing is not null) return existing;
        var userName = "google_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.Subject))).ToLowerInvariant()[..32];
        try
        {
            await using var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await using var check = Command(c, tx, "SELECT 1 FROM identity_users WHERE email=@email OR user_name=@name LIMIT 1",
                ("@email", identity.Email), ("@name", userName));
            if (await check.ExecuteScalarAsync(ct) is not null)
            {
                var winner = await Find(c, tx, identity.Subject, ct);
                if (winner is null) throw new ExternalLoginConflictException();
                await tx.CommitAsync(ct); return winner;
            }
            var userId = Guid.NewGuid();
            await using var insert = Command(c, tx, """
                INSERT INTO identity_users (id,user_name,email,password_hash,is_active,is_deleted,created_at)
                VALUES (@id,@name,@email,NULL,1,0,UTC_TIMESTAMP())
                """, ("@id", userId.ToString()), ("@name", userName), ("@email", identity.Email));
            await insert.ExecuteNonQueryAsync(ct);
            await InsertLink(c, tx, userId, identity.Subject, ct);
            await tx.CommitAsync(ct);
            return new User { Id = userId, UserName = userName, Email = identity.Email, PasswordHash = null, IsActive = true };
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            // Another first login may have won. Accept only the exact provider subject;
            // an email/username collision is never an implicit account link.
            return await Find(c, null, identity.Subject, ct) ?? throw new ExternalLoginConflictException();
        }
    }
    private static async Task InsertLink(MySqlConnection c, MySqlTransaction tx, Guid userId, string subject, CancellationToken ct)
    {
        await using var insert = Command(c, tx, """
            INSERT INTO identity_external_logins (id,user_id,provider,provider_subject,created_at)
            VALUES (@id,@user,'Google',@subject,UTC_TIMESTAMP(6))
            """, ("@id", Guid.NewGuid().ToString()), ("@user", userId.ToString()), ("@subject", subject));
        await insert.ExecuteNonQueryAsync(ct);
    }
    public async Task LinkGoogleAsync(Guid userId, VerifiedGoogleIdentity identity, CancellationToken ct)
    {
        await using var c = new MySqlConnection(connectionString); await c.OpenAsync(ct);
        try
        {
            await using var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await using var cmd = Command(c, tx, "SELECT * FROM identity_users WHERE id=@id FOR UPDATE", ("@id", userId.ToString()));
            User user;
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
                user = await reader.ReadAsync(ct) ? Read(reader) : throw new UnauthorizedAccessException("Account unavailable.");
            if (!user.IsActive) throw new UnauthorizedAccessException("Account unavailable.");
            if (!string.Equals(user.Email, identity.Email, StringComparison.OrdinalIgnoreCase)) throw new ExternalLoginConflictException();
            var existing = await Find(c, tx, identity.Subject, ct);
            if (existing is not null)
            {
                if (existing.Id != userId) throw new ExternalLoginConflictException();
                await tx.CommitAsync(ct); return;
            }
            await InsertLink(c, tx, userId, identity.Subject, ct);
            await tx.CommitAsync(ct);
        }
        catch (MySqlException ex) when (ex.Number == 1062) { throw new ExternalLoginConflictException(); }
    }
}
