using Microsoft.Extensions.Configuration;
using MySqlConnector;
namespace Zumaris.Document.Infrastructure;
internal sealed class PassportDb(IConfiguration config)
{
    public async Task<MySqlConnection> Open(CancellationToken ct) { var c = new MySqlConnection(config.GetConnectionString("DefaultConnection")); try { await c.OpenAsync(ct); return c; } catch { await c.DisposeAsync(); throw; } }
    public static MySqlCommand Cmd(MySqlConnection c, string sql, params (string, object?)[] values) { var cmd = new MySqlCommand(sql, c); foreach (var (k, v) in values) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value); return cmd; }
    public static string? Text(MySqlDataReader r, string n) => r.IsDBNull(r.GetOrdinal(n)) ? null : r.GetString(n);
    public static DateTime Utc(MySqlDataReader r, string n) => DateTime.SpecifyKind(r.GetDateTime(n), DateTimeKind.Utc);
}
