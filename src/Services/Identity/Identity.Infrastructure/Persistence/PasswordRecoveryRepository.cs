using System.Data;
using System.Security.Cryptography;
using System.Text;
using Identity.Application.Features.Auth;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
namespace Identity.Infrastructure.Persistence;
public sealed class PasswordRecoveryRepository(IConfiguration config) : IPasswordRecoveryRepository
{
    private async Task<MySqlConnection> Open(CancellationToken ct) { var c = new MySqlConnection(config.GetConnectionString("DefaultConnection")); try { await c.OpenAsync(ct); return c; } catch { await c.DisposeAsync(); throw; } }
    private static MySqlCommand Q(MySqlConnection c, MySqlTransaction? tx, string sql, params (string, object?)[] values) { var q = new MySqlCommand(sql, c, tx); foreach (var (k, v) in values) q.Parameters.AddWithValue(k, v ?? DBNull.Value); return q; }
    private static string Fingerprint(string hash, string email) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash + "\0" + email.Trim().ToLowerInvariant())));
    public async Task<bool> Issue(RecoveryIssue i, CancellationToken ct)
    {
        await using var c = await Open(ct); await using var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct); Guid user; string password; string storedEmail;
        await using (var q = Q(c, tx, $"SELECT id,password_hash,email FROM {AccountSql.Table(i.Application)} WHERE email=@email AND is_active=1 AND is_deleted=0 AND password_hash IS NOT NULL FOR UPDATE", ("@email", i.Email)))
        await using (var r = await q.ExecuteReaderAsync(ct)) { if (!await r.ReadAsync(ct)) return false; user = r.GetGuid(0); password = r.GetString(1); storedEmail = r.GetString(2).Trim().ToLowerInvariant(); if (string.IsNullOrWhiteSpace(password) || !string.Equals(storedEmail, i.Email, StringComparison.Ordinal)) return false; }
        await using (var cooldown = Q(c, tx, "SELECT MAX(created_at) FROM identity_password_resets WHERE application=@app AND user_id=@user", ("@app", i.Application), ("@user", user.ToString())))
        { var last = await cooldown.ExecuteScalarAsync(ct); if (last is DateTime date && date.AddSeconds(i.CooldownSeconds) > i.CreatedAtUtc) return false; }
        await using (var cancel = Q(c, tx, "UPDATE identity_password_resets SET used_at=@now,delivery_status='Cancelled',protected_payload=NULL WHERE application=@app AND user_id=@user AND used_at IS NULL", ("@now", i.CreatedAtUtc), ("@app", i.Application), ("@user", user.ToString()))) await cancel.ExecuteNonQueryAsync(ct);
        await using var insert = Q(c, tx, """
   INSERT INTO identity_password_resets(id,application,user_id,token_hash,credential_fingerprint,created_at,expires_at,protected_payload,delivery_status,next_attempt_at)
   VALUES(@id,@app,@user,@hash,@fingerprint,@now,@expires,@payload,'Queued',@now)
   """, ("@id", i.Id.ToString()), ("@app", i.Application), ("@user", user.ToString()), ("@hash", i.TokenHash), ("@fingerprint", Fingerprint(password,storedEmail)), ("@now", i.CreatedAtUtc), ("@expires", i.ExpiresAtUtc), ("@payload", i.ProtectedPayload));
        await insert.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct); return true;
    }
    public async Task<bool> Consume(string application, string tokenHash, string passwordHash, DateTime now, CancellationToken ct)
    {
        await using var c = await Open(ct); await using var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct); Guid user; string fingerprint;
        await using (var find = Q(c, tx, "SELECT user_id FROM identity_password_resets WHERE application=@app AND token_hash=@hash", ("@app", application), ("@hash", tokenHash)))
        { var value = await find.ExecuteScalarAsync(ct); if (value is null) return false; user = value is Guid g ? g : Guid.Parse((string)value); }
        // Account -> reset -> session lock order also serializes new logins with password reset.
        await using (var account = Q(c, tx, $"SELECT password_hash,email FROM {AccountSql.Table(application)} WHERE id=@id AND is_active=1 AND is_deleted=0 FOR UPDATE", ("@id", user.ToString())))
        await using (var reader = await account.ExecuteReaderAsync(ct)) { if (!await reader.ReadAsync(ct) || reader.IsDBNull(0)) return false; var current = reader.GetString(0); if (string.IsNullOrWhiteSpace(current)) return false; fingerprint = Fingerprint(current, reader.GetString(1)); }
        await using (var reset = Q(c, tx, "SELECT credential_fingerprint FROM identity_password_resets WHERE application=@app AND user_id=@user AND token_hash=@hash AND used_at IS NULL AND expires_at>UTC_TIMESTAMP(6) FOR UPDATE", ("@app", application), ("@user", user.ToString()), ("@hash", tokenHash), ("@now", now)))
        { var expected = await reset.ExecuteScalarAsync(ct); if (expected is not string text || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(text), Encoding.ASCII.GetBytes(fingerprint))) return false; }
        await using (var update = Q(c, tx, $"UPDATE {AccountSql.Table(application)} SET password_hash=@password,updated_at=@now WHERE id=@id", ("@password", passwordHash), ("@now", now), ("@id", user.ToString()))) await update.ExecuteNonQueryAsync(ct);
        await using (var consume = Q(c, tx, "UPDATE identity_password_resets SET used_at=@now,delivery_status='Cancelled',protected_payload=NULL WHERE application=@app AND user_id=@user AND used_at IS NULL", ("@now", now), ("@app", application), ("@user", user.ToString()))) await consume.ExecuteNonQueryAsync(ct);
        await using (var revoke = Q(c, tx, "UPDATE zumaris_auth_sessions SET revoked_at=@now,revocation_reason='password_reset' WHERE application=@app AND user_id=@user AND revoked_at IS NULL", ("@now", now), ("@app", application), ("@user", user.ToString()))) await revoke.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct); return true;
    }
    public async Task<RecoveryDelivery?> Claim(DateTime now, CancellationToken ct)
    {
        await using var c = await Open(ct); await using var tx = await c.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using (var expired = Q(c, tx, "UPDATE identity_password_resets SET delivery_status='Cancelled',protected_payload=NULL WHERE delivery_status='Queued' AND expires_at<=@now", ("@now", now))) await expired.ExecuteNonQueryAsync(ct);
        Guid id; string payload; int attempts;
        await using (var pick = Q(c, tx, "SELECT id,protected_payload,attempts FROM identity_password_resets WHERE delivery_status='Queued' AND used_at IS NULL AND expires_at>@now AND next_attempt_at<=@now AND (lease_until IS NULL OR lease_until<=@now) ORDER BY created_at,id LIMIT 1 FOR UPDATE SKIP LOCKED", ("@now", now)))
        await using (var r = await pick.ExecuteReaderAsync(ct)) { if (!await r.ReadAsync(ct)) return null; id = r.GetGuid(0); payload = r.GetString(1); attempts = r.GetInt32(2) + 1; }
        var lease = Guid.NewGuid(); await using var claim = Q(c, tx, "UPDATE identity_password_resets SET lease_id=@lease,lease_until=@until,attempts=attempts+1 WHERE id=@id", ("@lease", lease.ToString()), ("@until", now.AddMinutes(2)), ("@id", id.ToString())); await claim.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct); return new(id, lease, payload, attempts);
    }
    public async Task Complete(RecoveryDelivery delivery, bool sent, DateTime now, CancellationToken ct)
    {
        await using var c = await Open(ct); await using var update = Q(c, null, "UPDATE identity_password_resets SET delivery_status=@status,protected_payload=IF(@sent,NULL,protected_payload),sent_at=IF(@sent,@now,sent_at),lease_id=NULL,lease_until=NULL,next_attempt_at=@next WHERE id=@id AND lease_id=@lease AND delivery_status='Queued' AND used_at IS NULL", ("@status", sent ? "Sent" : "Queued"), ("@sent", sent), ("@now", now), ("@next", now.AddSeconds(Math.Min(900, 30 * Math.Pow(2, Math.Min(delivery.Attempts - 1, 5))))), ("@id", delivery.Id.ToString()), ("@lease", delivery.LeaseId.ToString())); await update.ExecuteNonQueryAsync(ct);
    }
}
