using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Document.Application;
namespace Zumaris.Document.Infrastructure;
public sealed class DocumentRepository(IConfiguration config) : IDocumentRepository
{
    private readonly PassportDb db = new(config);
    private static PropertyDocument Read(MySqlDataReader r) => new(r.GetGuid("id"), r.GetGuid("property_id"), JsonSerializer.Deserialize<DocumentInput>(r.GetString("details"))!, r.GetString("file_name"), r.GetString("content_type"), r.GetInt64("size_bytes"), r.GetString("sha256"), PassportDb.Utc(r, "uploaded_at"), r.IsDBNull(r.GetOrdinal("updated_at")) ? null : PassportDb.Utc(r, "updated_at"), r.GetBoolean("is_archived"));
    public async Task<(IReadOnlyList<PropertyDocument> Items, long Total)> List(Guid property, int page, int size, string? type, Guid? equipment, Guid? request, bool archived, CancellationToken ct)
    {
        await using var c = await db.Open(ct); const string f = " WHERE property_id=@p AND is_archived=@a AND (@type IS NULL OR document_type=@type) AND (@eq IS NULL OR equipment_id=@eq) AND (@req IS NULL OR request_id=@req)";
        MySqlCommand Q(string sql) => PassportDb.Cmd(c, sql, ("@p", property.ToString()), ("@a", archived), ("@type", type), ("@eq", equipment?.ToString()), ("@req", request?.ToString()), ("@size", size), ("@offset", (page - 1) * size)); await using var count = Q("SELECT COUNT(*) FROM property_documents" + f); long total = Convert.ToInt64(await count.ExecuteScalarAsync(ct)); await using var q = Q("SELECT * FROM property_documents" + f + " ORDER BY uploaded_at DESC,id DESC LIMIT @size OFFSET @offset"); await using var r = await q.ExecuteReaderAsync(ct); var rows = new List<PropertyDocument>(); while (await r.ReadAsync(ct)) rows.Add(Read(r)); return (rows, total);
    }
    public async Task<PropertyDocument?> Get(Guid property, Guid id, CancellationToken ct) { await using var c = await db.Open(ct); await using var q = PassportDb.Cmd(c, "SELECT * FROM property_documents WHERE property_id=@p AND id=@id", ("@p", property.ToString()), ("@id", id.ToString())); await using var r = await q.ExecuteReaderAsync(ct); return await r.ReadAsync(ct) ? Read(r) : null; }
    public Task<PropertyDocument> Create(Guid property, Guid id, DocumentInput input, string fileName, string contentType, long size, string sha256, CancellationToken ct) => Save(property, id, input, fileName, contentType, size, sha256, true, ct);
    public Task<PropertyDocument> Update(Guid property, Guid id, DocumentInput input, CancellationToken ct) => Save(property, id, input, null, null, 0, null, false, ct);
    private async Task<PropertyDocument> Save(Guid property, Guid id, DocumentInput input, string? fileName, string? contentType, long size, string? hash, bool create, CancellationToken ct)
    {
        await using var c = await db.Open(ct); await using var tx = await c.BeginTransactionAsync(ct); var now = DateTime.UtcNow;
        string sql = create ? "INSERT INTO property_documents(id,property_id,document_type,equipment_id,request_id,details,file_name,content_type,size_bytes,sha256,uploaded_at) VALUES(@id,@p,@type,@eq,@req,@details,@name,@mime,@size,@hash,@now)" : "UPDATE property_documents SET document_type=@type,equipment_id=@eq,request_id=@req,details=@details,updated_at=@now WHERE id=@id AND property_id=@p AND is_archived=0";
        await using var q = PassportDb.Cmd(c, sql, ("@id", id.ToString()), ("@p", property.ToString()), ("@type", input.Type), ("@eq", input.EquipmentId?.ToString()), ("@req", input.RequestId?.ToString()), ("@details", JsonSerializer.Serialize(input)), ("@name", fileName), ("@mime", contentType), ("@size", size), ("@hash", hash), ("@now", now)); q.Transaction = tx; if (await q.ExecuteNonQueryAsync(ct) != 1) throw new KeyNotFoundException();
        await using var e = PassportDb.Cmd(c, "INSERT INTO document_events(id,property_id,document_id,event_type,title,occurred_at)VALUES(@e,@p,@id,@type,@title,@now)", ("@e", Guid.NewGuid().ToString()), ("@p", property.ToString()), ("@id", id.ToString()), ("@type", create ? "DocumentUploaded" : "DocumentUpdated"), ("@title", input.Title), ("@now", now)); e.Transaction = tx; await e.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct); return (await Get(property, id, ct))!;
    }
    public async Task Archive(Guid property, Guid id, CancellationToken ct)
    {
        await using var c = await db.Open(ct); await using var tx = await c.BeginTransactionAsync(ct); await using var q = PassportDb.Cmd(c, "UPDATE property_documents SET is_archived=1,updated_at=UTC_TIMESTAMP(6) WHERE id=@id AND property_id=@p AND is_archived=0", ("@id", id.ToString()), ("@p", property.ToString())); q.Transaction = tx;
        if (await q.ExecuteNonQueryAsync(ct) == 1) { await using var e = PassportDb.Cmd(c, "INSERT INTO document_events(id,property_id,document_id,event_type,title,occurred_at) SELECT @e,property_id,id,'DocumentArchived',JSON_UNQUOTE(JSON_EXTRACT(details,'$.Title')),UTC_TIMESTAMP(6) FROM property_documents WHERE id=@id", ("@e", Guid.NewGuid().ToString()), ("@id", id.ToString())); e.Transaction = tx; await e.ExecuteNonQueryAsync(ct); }
        await tx.CommitAsync(ct);
    }
    public async Task<(IReadOnlyList<DocumentEvent> Items, long Total)> Events(Guid property, int page, int size, DateTime? from, DateTime? to, string? type, CancellationToken ct)
    {
        await using var c = await db.Open(ct); const string f = " WHERE property_id=@p AND (@from IS NULL OR occurred_at>=@from) AND (@to IS NULL OR occurred_at<=@to) AND (@type IS NULL OR BINARY event_type=BINARY @type)";
        MySqlCommand Q(string sql) => PassportDb.Cmd(c, sql, ("@p", property.ToString()), ("@from", from), ("@to", to), ("@type", type), ("@size", size), ("@offset", (page - 1) * size)); await using var count = Q("SELECT COUNT(*) FROM document_events" + f); long total = Convert.ToInt64(await count.ExecuteScalarAsync(ct)); await using var cmd = Q("SELECT * FROM document_events" + f + " ORDER BY occurred_at DESC,id DESC LIMIT @size OFFSET @offset"); await using var r = await cmd.ExecuteReaderAsync(ct); var rows = new List<DocumentEvent>(); while (await r.ReadAsync(ct)) rows.Add(new(r.GetGuid("id"), property, r.GetGuid("document_id"), r.GetString("event_type"), r.GetString("title"), PassportDb.Utc(r, "occurred_at"))); return (rows, total);
    }
}
