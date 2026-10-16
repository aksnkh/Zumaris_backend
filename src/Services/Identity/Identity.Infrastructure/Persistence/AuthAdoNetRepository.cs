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
