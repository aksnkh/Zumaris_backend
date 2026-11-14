using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Maintenance.Application;
namespace Zumaris.Maintenance.Infrastructure;
public sealed class EquipmentRepository(IConfiguration config) : IEquipmentRepository
{
    private readonly PassportDb db = new(config);
    private static Equipment Read(MySqlDataReader r) => new(r.GetGuid("id"), r.GetGuid("property_id"), JsonSerializer.Deserialize<EquipmentInput>(r.GetString("details"))!, PassportDb.Utc(r, "created_at"), r.IsDBNull(r.GetOrdinal("updated_at")) ? null : PassportDb.Utc(r, "updated_at"), r.GetBoolean("is_archived"));
    public async Task<(IReadOnlyList<Equipment> Items, long Total)> List(Guid property, int page, int size, string? category, string? status, bool archived, CancellationToken ct)
    {
        await using var c = await db.Open(ct); const string filter = " WHERE property_id=@p AND is_archived=@a AND (@category IS NULL OR category=@category) AND (@status IS NULL OR status=@status)";
        MySqlCommand Q(string sql) => PassportDb.Cmd(c, sql, ("@p", property.ToString()), ("@a", archived), ("@category", category), ("@status", status), ("@offset", (page - 1) * size), ("@size", size));
        await using var count = Q("SELECT COUNT(*) FROM equipment" + filter); long total = Convert.ToInt64(await count.ExecuteScalarAsync(ct));
        await using var cmd = Q("SELECT * FROM equipment" + filter + " ORDER BY created_at DESC,id DESC LIMIT @size OFFSET @offset"); await using var r = await cmd.ExecuteReaderAsync(ct); var items = new List<Equipment>(); while (await r.ReadAsync(ct)) items.Add(Read(r)); return (items, total);
    }
    public async Task<Equipment?> Get(Guid property, Guid id, CancellationToken ct) { await using var c = await db.Open(ct); await using var q = PassportDb.Cmd(c, "SELECT * FROM equipment WHERE property_id=@p AND id=@id", ("@p", property.ToString()), ("@id", id.ToString())); await using var r = await q.ExecuteReaderAsync(ct); return await r.ReadAsync(ct) ? Read(r) : null; }
    public async Task<Equipment> Save(Guid property, Guid? id, EquipmentInput input, CancellationToken ct)
    {
        await using var c = await db.Open(ct); await using var tx = await c.BeginTransactionAsync(ct); var key = id ?? Guid.NewGuid(); var now = DateTime.UtcNow;
        var sql = id is null ? "INSERT INTO equipment(id,property_id,category,status,details,created_at) VALUES(@id,@p,@category,@status,@details,@now)" : "UPDATE equipment SET category=@category,status=@status,details=@details,updated_at=@now WHERE property_id=@p AND id=@id AND is_archived=0";
        await using var q = PassportDb.Cmd(c, sql, ("@id", key.ToString()), ("@p", property.ToString()), ("@category", input.Category), ("@status", input.Status), ("@details", JsonSerializer.Serialize(input)), ("@now", now)); q.Transaction = tx;
        if (await q.ExecuteNonQueryAsync(ct) != 1) throw new KeyNotFoundException();
        await using var e = PassportDb.Cmd(c, "INSERT INTO equipment_events(id,property_id,equipment_id,event_type,title,occurred_at) VALUES(@event,@p,@id,@type,@title,@now)", ("@event", Guid.NewGuid().ToString()), ("@p", property.ToString()), ("@id", key.ToString()), ("@type", id is null ? "EquipmentCreated" : "EquipmentUpdated"), ("@title", input.Name), ("@now", now)); e.Transaction = tx; await e.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct); return (await Get(property, key, ct))!;
    }
    public async Task Archive(Guid property, Guid id, CancellationToken ct)
    {
        await using var c = await db.Open(ct); await using var tx = await c.BeginTransactionAsync(ct);
        await using var q = PassportDb.Cmd(c, "UPDATE equipment SET is_archived=1,updated_at=UTC_TIMESTAMP(6) WHERE property_id=@p AND id=@id AND is_archived=0", ("@p", property.ToString()), ("@id", id.ToString())); q.Transaction = tx;
        if (await q.ExecuteNonQueryAsync(ct) == 1) { await using var e = PassportDb.Cmd(c, "INSERT INTO equipment_events(id,property_id,equipment_id,event_type,title,occurred_at) SELECT @e,property_id,id,'EquipmentArchived',JSON_UNQUOTE(JSON_EXTRACT(details,'$.Name')),UTC_TIMESTAMP(6) FROM equipment WHERE id=@id", ("@e", Guid.NewGuid().ToString()), ("@id", id.ToString())); e.Transaction = tx; await e.ExecuteNonQueryAsync(ct); }
        await tx.CommitAsync(ct);
    }
    public async Task<(IReadOnlyList<EquipmentEvent> Items, long Total)> Events(Guid property, int page, int size, DateTime? from, DateTime? to, string? type, CancellationToken ct)
    {
        await using var c = await db.Open(ct); const string f = " WHERE property_id=@p AND (@from IS NULL OR occurred_at>=@from) AND (@to IS NULL OR occurred_at<=@to) AND (@type IS NULL OR BINARY event_type=BINARY @type)";
        MySqlCommand Q(string sql) => PassportDb.Cmd(c, sql, ("@p", property.ToString()), ("@from", from), ("@to", to), ("@type", type), ("@size", size), ("@offset", (page - 1) * size)); await using var count = Q("SELECT COUNT(*) FROM equipment_events" + f); long total = Convert.ToInt64(await count.ExecuteScalarAsync(ct)); await using var cmd = Q("SELECT * FROM equipment_events" + f + " ORDER BY occurred_at DESC,id DESC LIMIT @size OFFSET @offset"); await using var r = await cmd.ExecuteReaderAsync(ct); var rows = new List<EquipmentEvent>(); while (await r.ReadAsync(ct)) rows.Add(new(r.GetGuid("id"), property, r.GetGuid("equipment_id"), r.GetString("event_type"), r.GetString("title"), PassportDb.Utc(r, "occurred_at"))); return (rows, total);
    }
}
