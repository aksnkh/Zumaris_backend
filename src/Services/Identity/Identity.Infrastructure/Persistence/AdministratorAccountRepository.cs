using System.Data;
using Identity.Application.Features.Administration;
using Identity.Application.Features.Signup;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace Identity.Infrastructure.Persistence;

public sealed class AdministratorAccountRepository(IConfiguration configuration) : IAdministratorAccountRepository
{
    private static MySqlCommand Command(MySqlConnection c, MySqlTransaction tx, string sql, params (string, object?)[] values)
    {
        var command = new MySqlCommand(sql, c, tx);
        foreach (var (key, value) in values) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return command;
    }

    public async Task<AdministratorAccountResponse> CreateAsync(SignupAccount account, Guid? actorUserId, DateTime now, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        // A seeded singleton row serializes both bootstrap and authenticated administrator creation.
        bool completed;
        await using (var command = Command(connection, tx, "SELECT completed_at FROM identity_administrator_setup WHERE id=1 FOR UPDATE"))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new SignupException(503, "Administrator account setup is unavailable.");
            completed = !reader.IsDBNull(0);
        }
        if (actorUserId is null)
        {
            if (completed) throw new SignupException(409, "Administrator bootstrap has already been completed.");
            // Even disabled/manual memberships close the first-administrator path. This is not account recovery.
            await using var existing = Command(connection, tx, "SELECT user_id FROM platform_administrators LIMIT 1 FOR UPDATE");
            if (await existing.ExecuteScalarAsync(ct) is not null)
                throw new SignupException(409, "An administrator already exists. Ask an authorized administrator to create this account.");
        }
        else
        {
            // Recheck membership and the real account inside the write transaction, not just in the API filter.
            await using var actor = Command(connection, tx, """
                SELECT a.user_id FROM platform_administrators a JOIN identity_users u ON u.id=a.user_id
                WHERE a.user_id=@actor AND a.is_active=1 AND u.is_active=1 AND u.is_deleted=0 FOR UPDATE
                """, ("@actor", actorUserId.Value.ToString()));
            if (await actor.ExecuteScalarAsync(ct) is null)
                throw new SignupException(403, "Active administrator access is required to create an administrator account.");
        }

        var user = await SignupRepository.InsertAsync(connection, tx, account, ct, now);
        await using (var membership = Command(connection, tx,
            "INSERT INTO platform_administrators(user_id,is_active,granted_at)VALUES(@user,1,@now)",
            ("@user", user.Id.ToString()), ("@now", now))) await membership.ExecuteNonQueryAsync(ct);
        await using (var audit = Command(connection, tx, """
            INSERT INTO identity_administrator_account_creations(user_id,created_by_user_id,source,created_at)
            VALUES(@user,@actor,@source,@now)
            """, ("@user", user.Id.ToString()), ("@actor", actorUserId?.ToString()),
            ("@source", actorUserId is null ? "Bootstrap" : "Administrator"), ("@now", now)))
            await audit.ExecuteNonQueryAsync(ct);
        await using (var complete = Command(connection, tx, """
            UPDATE identity_administrator_setup SET completed_at=@now,first_administrator_user_id=@user
            WHERE id=1 AND completed_at IS NULL
            """, ("@now", now), ("@user", (actorUserId ?? user.Id).ToString())))
            await complete.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return new(user.Id, user.UserName, user.Email, account.FirstName, account.LastName, account.Phone,
            "Admin", now, actorUserId);
    }
}
