using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.ServiceCatalog.Application.Abstractions;
using Zumaris.ServiceCatalog.Domain.Entities;
namespace Zumaris.ServiceCatalog.Infrastructure.Persistence;

public sealed class CategoryAdoNetRepository(IConfiguration configuration) : ICategoryRepository
{
    public async Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is required.");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("SELECT id, name, icon FROM categories WHERE id = @id", connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new Category(reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2))
            : null;
    }

    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is required.");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("SELECT id, name, icon FROM categories ORDER BY id", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<Category>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new Category(reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return result;
    }
}
