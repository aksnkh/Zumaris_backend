using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.ServiceCatalog.Application.Abstractions;
using Zumaris.ServiceCatalog.Domain.Entities;
namespace Zumaris.ServiceCatalog.Infrastructure.Persistence;

public sealed class AreaAdoNetRepository(IConfiguration configuration) : IAreaRepository
{
    public async Task<IReadOnlyList<Area>> ListAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is required.");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("SELECT id, name, icon FROM areas ORDER BY id", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<Area>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new Area(reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return result;
    }
}
