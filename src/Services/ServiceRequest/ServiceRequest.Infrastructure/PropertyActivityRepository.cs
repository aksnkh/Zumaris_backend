using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.ServiceRequests.Application;
namespace Zumaris.ServiceRequests.Infrastructure;
public sealed class PropertyActivityRepository(IConfiguration config) : IPropertyActivityRepository
{
    private async Task<MySqlConnection> Open(CancellationToken ct) { var c = new MySqlConnection(config.GetConnectionString("DefaultConnection")); try { await c.OpenAsync(ct); return c; } catch { await c.DisposeAsync(); throw; } }
    private static MySqlCommand Q(MySqlConnection c, string sql, params (string, object?)[] ps) { var q = new MySqlCommand(sql, c); foreach (var (k, v) in ps) q.Parameters.AddWithValue(k, v ?? DBNull.Value); return q; }
    public async Task<PropertyServiceSummary> Summary(Guid actor, Guid property, CancellationToken ct) { await using var c = await Open(ct); await using var q = Q(c, """
 SELECT COUNT(*) AS total,COALESCE(SUM(status IN ('Pending','Assigned','InProgress')),0) AS open_count,
 COALESCE(SUM(status IN ('Pending','Assigned') AND preferred_date>=UTC_DATE()),0) AS upcoming,
 COALESCE(SUM(status='Completed'),0) AS completed,COALESCE(SUM(status='Cancelled'),0) AS cancelled
 FROM service_requests WHERE property_id=@p AND user_id=@u
 """, ("@p", property.ToString()), ("@u", actor.ToString())); await using var r = await q.ExecuteReaderAsync(ct); await r.ReadAsync(ct); return new(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4)); }
    public async Task<(IReadOnlyList<PropertyServiceEvent> Items, long Total)> Events(Guid actor, Guid property, int page, int size, DateTime? from, DateTime? to, string? type, CancellationToken ct)
    {
        await using var c = await Open(ct); const string join = " FROM service_request_status_history h JOIN service_requests r ON r.id=h.request_id WHERE r.property_id=@p AND r.user_id=@u AND (@from IS NULL OR h.occurred_at>=@from) AND (@to IS NULL OR h.occurred_at<=@to) AND (@type IS NULL OR BINARY CONCAT('Request',h.to_status)=BINARY @type)";
        MySqlCommand Query(string sql) => Q(c, sql, ("@p", property.ToString()), ("@u", actor.ToString()), ("@from", from), ("@to", to), ("@type", type), ("@size", size), ("@offset", (page - 1) * size)); await using var count = Query("SELECT COUNT(*)" + join); var total = Convert.ToInt64(await count.ExecuteScalarAsync(ct)); await using var q = Query("SELECT h.id,h.request_id,r.request_number,h.to_status,h.reason,h.occurred_at" + join + " ORDER BY h.occurred_at DESC,h.id DESC LIMIT @size OFFSET @offset"); await using var reader = await q.ExecuteReaderAsync(ct); var list = new List<PropertyServiceEvent>(); while (await reader.ReadAsync(ct)) list.Add(new(reader.GetGuid(0), reader.GetGuid(1), property, reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc))); return (list, total);
    }
}
