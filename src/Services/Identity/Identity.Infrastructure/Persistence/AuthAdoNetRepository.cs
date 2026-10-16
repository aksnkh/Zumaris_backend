using System.Data;
using Identity.Application.Abstractions.Persistence;
using Identity.Domain.Entities;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Identity.Domain.Entities;

namespace Identity.Infrastructure.Persistence;

public sealed class AuthAdoNetRepository : IAuthRepository
{
    private readonly string _connectionString;

    public AuthAdoNetRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    private MySqlConnection CreateConnection() => new(_connectionString);

    public async Task<User?> GetUserByUsernameAsync(string userName, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand("sp_identity_get_user_by_username", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("p_user_name", userName);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new User
            {
                Id = reader.GetGuid(reader.GetOrdinal("id")),
                UserName = reader.GetString("user_name"),
                Email = reader.GetString("email"),
                PasswordHash = reader.GetString("password_hash"),
                IsActive = reader.GetBoolean("is_active")
            };
        }

        return null;
    }

    public async Task InsertRefreshTokenAsync(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTime expiresAtUtc,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand("sp_identity_insert_refresh_token", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("p_id", id.ToString());
        command.Parameters.AddWithValue("p_user_id", userId.ToString());
        command.Parameters.AddWithValue("p_token_hash", tokenHash);
        command.Parameters.AddWithValue("p_expires_at", expiresAtUtc);
        command.Parameters.AddWithValue("p_created_at", createdAtUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<RefreshTokenRecord?> GetRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand("sp_identity_get_refresh_token", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("p_token_hash", tokenHash);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new RefreshTokenRecord
            {
                Id = Guid.Parse(reader.GetString("refresh_token_id")),
                UserId = Guid.Parse(reader.GetString("user_id")),
                TokenHash = reader.GetString("token_hash"),
                ExpiresAtUtc = reader.GetDateTime("expires_at"),
                CreatedAtUtc = reader.GetDateTime("created_at"),
                RevokedAtUtc = reader.IsDBNull(reader.GetOrdinal("revoked_at"))
                    ? null
                    : reader.GetDateTime("revoked_at"),
                ReplacedByTokenHash = reader.IsDBNull(reader.GetOrdinal("replaced_by_token_hash"))
                    ? null
                    : reader.GetString("replaced_by_token_hash")
            };
        }

        return null;
    }

    public async Task<bool> RotateRefreshTokenAsync(
        string oldTokenHash,
        Guid newId,
        Guid userId,
        string newTokenHash,
        DateTime newExpiresAtUtc,
        DateTime newCreatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand("sp_identity_rotate_refresh_token", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("p_old_token_hash", oldTokenHash);
        command.Parameters.AddWithValue("p_new_id", newId.ToString());
        command.Parameters.AddWithValue("p_user_id", userId.ToString());
        command.Parameters.AddWithValue("p_new_token_hash", newTokenHash);
        command.Parameters.AddWithValue("p_new_expires_at", newExpiresAtUtc);
        command.Parameters.AddWithValue("p_new_created_at", newCreatedAtUtc);

        var outSuccessParam = new MySqlParameter("p_is_success", MySqlDbType.Byte)
        {
            Direction = ParameterDirection.Output
        };
        command.Parameters.Add(outSuccessParam);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return Convert.ToBoolean(outSuccessParam.Value);
    }

    public async Task RevokeRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand("sp_identity_revoke_refresh_token", connection)
        {
            CommandType = CommandType.StoredProcedure
        };

        command.Parameters.AddWithValue("p_token_hash", tokenHash);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> RegisterAsync(string username, string passwordHash, string email)
    {
        using var connection = new MySqlConnection(_connectionString);
        using var command = new MySqlCommand("sp_identity_register_user", connection);

        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("p_user_name", username);
        command.Parameters.AddWithValue("p_password_hash", passwordHash);
        command.Parameters.AddWithValue("p_email", email);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
        return true;
    }

}
