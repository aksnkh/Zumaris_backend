using System.Data;
using Identity.Application.Abstractions.Persistence;
using Identity.Domain.Entities;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace Identity.Infrastructure.Persistence;

public sealed class SessionAdoNetRepository(IConfiguration configuration) : ISessionRepository
{
    private readonly string connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new MySqlConnection(connectionString);
        try { await connection.OpenAsync(ct); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static MySqlCommand Command(MySqlConnection connection, string sql,
        MySqlTransaction? transaction = null, params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private static AuthSession ReadSession(MySqlDataReader reader) => new(
        Guid.Parse(reader.GetString("id")), Guid.Parse(reader.GetString("user_id")),
        reader.GetString("user_name"), DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        DateTime.SpecifyKind(reader.GetDateTime("expires_at"), DateTimeKind.Utc),
        reader.IsDBNull(reader.GetOrdinal("revoked_at")) ? null :
            DateTime.SpecifyKind(reader.GetDateTime("revoked_at"), DateTimeKind.Utc));

    public async Task CreateAsync(AuthSession session, string tokenHash, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var insert = Command(connection, """
            INSERT INTO zumaris_auth_sessions (id, user_id, user_name, created_at, expires_at)
            VALUES (@id, @user, @name, @created, @expires)
            """, transaction, ("@id", session.Id.ToString()), ("@user", session.UserId.ToString()),
            ("@name", session.UserName), ("@created", session.CreatedAtUtc), ("@expires", session.ExpiresAtUtc));
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await using var token = Command(connection, """
            INSERT INTO zumaris_auth_refresh_tokens (token_hash, session_id, created_at)
            VALUES (@hash, @id, UTC_TIMESTAMP(6))
            """, transaction, ("@hash", tokenHash), ("@id", session.Id.ToString()));
        await token.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<AuthSession?> FindByTokenAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        // Retain consumed token hashes so a replay can still find and revoke its session.
        await using var command = Command(connection, """
            SELECT s.* FROM zumaris_auth_sessions s
            INNER JOIN zumaris_auth_refresh_tokens t ON t.session_id = s.id
            WHERE t.token_hash = @hash
            """, null, ("@hash", tokenHash));
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSession(reader) : null;
    }

    public async Task<bool> RotateAsync(Guid sessionId, string oldHash, string newHash,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        // Always lock the session before its tokens: simultaneous refresh/logout requests serialize.
        await using var session = Command(connection, """
            SELECT (revoked_at IS NULL AND expires_at > UTC_TIMESTAMP(6))
            FROM zumaris_auth_sessions WHERE id = @id FOR UPDATE
            """, transaction, ("@id", sessionId.ToString()));
        var active = await session.ExecuteScalarAsync(cancellationToken);
        if (active is null || !Convert.ToBoolean(active)) return false;

        await using var token = Command(connection, """
            SELECT (used_at IS NOT NULL) FROM zumaris_auth_refresh_tokens
            WHERE token_hash = @hash AND session_id = @id FOR UPDATE
            """, transaction, ("@hash", oldHash), ("@id", sessionId.ToString()));
        var consumed = await token.ExecuteScalarAsync(cancellationToken);
        if (consumed is null) return false;
        if (Convert.ToBoolean(consumed))
        {
            await using var revoke = Command(connection, """
                UPDATE zumaris_auth_sessions SET revoked_at = UTC_TIMESTAMP(6), revocation_reason = 'token_reuse'
                WHERE id = @id
                """, transaction, ("@id", sessionId.ToString()));
            await revoke.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await using var consume = Command(connection, """
            UPDATE zumaris_auth_refresh_tokens SET used_at = UTC_TIMESTAMP(6)
            WHERE token_hash = @hash AND session_id = @id AND used_at IS NULL
            """, transaction, ("@hash", oldHash), ("@id", sessionId.ToString()));
        if (await consume.ExecuteNonQueryAsync(cancellationToken) != 1) return false;
        await using var insert = Command(connection, """
            INSERT INTO zumaris_auth_refresh_tokens (token_hash, session_id, created_at)
            VALUES (@hash, @id, UTC_TIMESTAMP(6))
            """, transaction, ("@hash", newHash), ("@id", sessionId.ToString()));
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> IsActiveAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, """
            SELECT 1 FROM zumaris_auth_sessions
            WHERE id = @id AND user_id = @user AND revoked_at IS NULL AND expires_at > UTC_TIMESTAMP(6)
            """, null, ("@id", sessionId.ToString()), ("@user", userId.ToString()));
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task<IReadOnlyList<AuthSession>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, """
            SELECT * FROM zumaris_auth_sessions
            WHERE user_id = @user AND revoked_at IS NULL AND expires_at > UTC_TIMESTAMP(6)
            ORDER BY created_at DESC
            """, null, ("@user", userId.ToString()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var sessions = new List<AuthSession>();
        while (await reader.ReadAsync(cancellationToken)) sessions.Add(ReadSession(reader));
        return sessions;
    }

    public async Task RevokeAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, """
            UPDATE zumaris_auth_sessions SET revoked_at = UTC_TIMESTAMP(6), revocation_reason = 'logout'
            WHERE id = @id AND user_id = @user AND revoked_at IS NULL
            """, null, ("@id", sessionId.ToString()), ("@user", userId.ToString()));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, """
            UPDATE zumaris_auth_sessions SET revoked_at = UTC_TIMESTAMP(6), revocation_reason = 'logout_all'
            WHERE user_id = @user AND revoked_at IS NULL
            """, null, ("@user", userId.ToString()));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
