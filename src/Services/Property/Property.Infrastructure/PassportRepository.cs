using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Property.Application.Features;
namespace Zumaris.Property.Infrastructure;
public sealed class PassportRepository(IConfiguration config) : IPassportRepository
{
    private readonly PassportDb db = new(config);
    public async Task<PassportProfile> Profile(Guid p, CancellationToken ct) { await using var c = await db.Open(ct); await using var q = PassportDb.Cmd(c, "SELECT floor_area_sqm FROM property_passport_profiles WHERE property_id=@p", ("@p", p.ToString())); var v = await q.ExecuteScalarAsync(ct); return new(v is null or DBNull ? null : Convert.ToDecimal(v)); }
    public async Task SetProfile(Guid p, PassportProfile input, CancellationToken ct) { await using var c = await db.Open(ct); await using var q = PassportDb.Cmd(c, "INSERT INTO property_passport_profiles(property_id,floor_area_sqm)VALUES(@p,@a) ON DUPLICATE KEY UPDATE floor_area_sqm=@a", ("@p", p.ToString()), ("@a", input.FloorAreaSquareMetres)); await q.ExecuteNonQueryAsync(ct); }
    private static PropertyRoom ReadRoom(MySqlDataReader r) => new(r.GetGuid("id"), r.GetGuid("property_id"), JsonSerializer.Deserialize<RoomInput>(r.GetString("details"))!, PassportDb.Utc(r, "created_at"), r.IsDBNull(r.GetOrdinal("updated_at")) ? null : PassportDb.Utc(r, "updated_at"), r.GetBoolean("is_archived"));
    private static ManualPropertyEvent ReadEvent(MySqlDataReader r) => new(r.GetGuid("id"), r.GetGuid("property_id"), JsonSerializer.Deserialize<ManualEventInput>(r.GetString("details"))!, PassportDb.Utc(r, "created_at"), r.IsDBNull(r.GetOrdinal("updated_at")) ? null : PassportDb.Utc(r, "updated_at"), r.GetBoolean("is_archived"));
    public async Task<(IReadOnlyList<PropertyRoom> Items, long Total)> Rooms(Guid p, int page, int size, bool archived, CancellationToken ct)
    {
        await using var c = await db.Open(ct); MySqlCommand Q(string sql) => PassportDb.Cmd(c, sql, ("@p", p.ToString()), ("@a", archived), ("@size", size), ("@offset", (page - 1) * size)); await using var count = Q("SELECT COUNT(*) FROM property_rooms WHERE property_id=@p AND is_archived=@a"); long total = Convert.ToInt64(await count.ExecuteScalarAsync(ct)); await using var q = Q("SELECT * FROM property_rooms WHERE property_id=@p AND is_archived=@a ORDER BY created_at DESC,id DESC LIMIT @size OFFSET @offset"); await using var r = await q.ExecuteReaderAsync(ct); var list = new List<PropertyRoom>(); while (await r.ReadAsync(ct)) list.Add(ReadRoom(r)); return (list, total);
    }
    public async Task<PropertyRoom?> Room(Guid p, Guid id, CancellationToken ct) { await using var c = await db.Open(ct); await using var q = PassportDb.Cmd(c, "SELECT * FROM property_rooms WHERE property_id=@p AND id=@id", ("@p", p.ToString()), ("@id", id.ToString())); await using var r = await q.ExecuteReaderAsync(ct); return await r.ReadAsync(ct) ? ReadRoom(r) : null; }
    public async Task<PropertyRoom> SaveRoom(Guid p, Guid? id, RoomInput input, CancellationToken ct) { var key = id ?? Guid.NewGuid(); await Save("property_rooms", p, key, JsonSerializer.Serialize(input), id is null, null, ct); return (await Room(p, key, ct))!; }
    public Task ArchiveRoom(Guid p, Guid id, CancellationToken ct) => Archive("property_rooms", p, id, ct);
    public async Task<(IReadOnlyList<ManualPropertyEvent> Items, long Total)> Events(Guid p, int page, int size, DateTime? from, DateTime? to, CancellationToken ct)
    {
        await using var c = await db.Open(ct); const string f = " WHERE property_id=@p AND is_archived=0 AND (@from IS NULL OR event_at>=@from) AND (@to IS NULL OR event_at<=@to)"; MySqlCommand Q(string sql) => PassportDb.Cmd(c, sql, ("@p", p.ToString()), ("@from", from), ("@to", to), ("@size", size), ("@offset", (page - 1) * size)); await using var count = Q("SELECT COUNT(*) FROM property_manual_events" + f); long total = Convert.ToInt64(await count.ExecuteScalarAsync(ct)); await using var q = Q("SELECT * FROM property_manual_events" + f + " ORDER BY event_at DESC,id DESC LIMIT @size OFFSET @offset"); await using var r = await q.ExecuteReaderAsync(ct); var list = new List<ManualPropertyEvent>(); while (await r.ReadAsync(ct)) list.Add(ReadEvent(r)); return (list, total);
    }
    public async Task<ManualPropertyEvent?> Event(Guid p, Guid id, CancellationToken ct) { await using var c = await db.Open(ct); await using var q = PassportDb.Cmd(c, "SELECT * FROM property_manual_events WHERE property_id=@p AND id=@id", ("@p", p.ToString()), ("@id", id.ToString())); await using var r = await q.ExecuteReaderAsync(ct); return await r.ReadAsync(ct) ? ReadEvent(r) : null; }
    public async Task<ManualPropertyEvent> SaveEvent(Guid p, Guid? id, ManualEventInput input, CancellationToken ct) { var key = id ?? Guid.NewGuid(); await Save("property_manual_events", p, key, JsonSerializer.Serialize(input), id is null, input.EventAt.UtcDateTime, ct); return (await Event(p, key, ct))!; }
    public Task ArchiveEvent(Guid p, Guid id, CancellationToken ct) => Archive("property_manual_events", p, id, ct);
    private async Task Save(string table, Guid p, Guid id, string details, bool create, DateTime? eventAt, CancellationToken ct)
    {
        // table is selected only by the fixed methods above, never by user input.
        await using var c = await db.Open(ct); string dateColumns = eventAt.HasValue ? ",event_at" : ""; string dateValues = eventAt.HasValue ? ",@event" : "";
        var sql = create ? $"INSERT INTO {table}(id,property_id,details,created_at{dateColumns})VALUES(@id,@p,@d,UTC_TIMESTAMP(6){dateValues})" : $"UPDATE {table} SET details=@d,updated_at=UTC_TIMESTAMP(6)" + (eventAt.HasValue ? ",event_at=@event" : "") + " WHERE property_id=@p AND id=@id AND is_archived=0";
        await using var q = PassportDb.Cmd(c, sql, ("@id", id.ToString()), ("@p", p.ToString()), ("@d", details), ("@event", eventAt)); if (await q.ExecuteNonQueryAsync(ct) != 1) throw new KeyNotFoundException();
    }
    private async Task Archive(string table, Guid p, Guid id, CancellationToken ct) { await using var c = await db.Open(ct); await using var q = PassportDb.Cmd(c, $"UPDATE {table} SET is_archived=1,updated_at=UTC_TIMESTAMP(6) WHERE property_id=@p AND id=@id", ("@p", p.ToString()), ("@id", id.ToString())); await q.ExecuteNonQueryAsync(ct); }
}
