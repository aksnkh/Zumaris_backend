using System.Data;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.ServiceRequests.Application;
using Zumaris.ServiceRequests.Domain;

namespace Zumaris.ServiceRequests.Infrastructure;

public sealed class RequestLifecycleAdoNetRepository(IConfiguration configuration) : IRequestLifecycleRepository
{
    private readonly string connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection is required.");
    private async Task<MySqlConnection> Open(CancellationToken ct)
    {
        var connection = new MySqlConnection(connectionString);
        try { await connection.OpenAsync(ct); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction,
        string sql, params (string Name, object? Value)[] parameters)
    {
        var cmd = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static async Task<ServiceRequestRecord?> ReadOne(MySqlCommand command, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
        return await reader.ReadAsync(ct) ? ServiceRequestAdoNetRepository.ReadRecord(reader) : null;
    }
    public async Task<ServiceRequestRecord?> GetAsync(Guid userId, Guid requestId, CancellationToken ct)
    {
        await using var c = await Open(ct);
        await using var cmd = Command(c, null, "SELECT * FROM service_requests WHERE id=@id AND user_id=@user", ("@id", requestId.ToString()), ("@user", userId.ToString()));
        return await ReadOne(cmd, ct);
    }
    public async Task<(IReadOnlyList<ServiceRequestRecord> Items, long Total)> ListAsync(Guid userId, RequestStatus? status, int page, int pageSize, CancellationToken ct)
    {
        await using var c = await Open(ct);
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        const string filter = "user_id=@user AND (@status IS NULL OR status=@status)";
        await using var count = Command(c, tx, "SELECT COUNT(*) FROM service_requests WHERE " + filter, ("@user", userId.ToString()), ("@status", status?.ToString()));
        var total = Convert.ToInt64(await count.ExecuteScalarAsync(ct));
        await using var cmd = Command(c, tx, "SELECT * FROM service_requests WHERE " + filter + " ORDER BY created_at DESC, id DESC LIMIT @limit OFFSET @offset",
            ("@user", userId.ToString()), ("@status", status?.ToString()), ("@limit", pageSize), ("@offset", (long)(page - 1) * pageSize));
        var items = new List<ServiceRequestRecord>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) items.Add(ServiceRequestAdoNetRepository.ReadRecord(r));
        await tx.CommitAsync(ct);
        return (items, total);
    }
    public async Task<IReadOnlyList<PhotoRecord>> PhotosAsync(Guid userId, IReadOnlyList<Guid> requestIds, CancellationToken ct)
    {
        if (requestIds.Count == 0) return [];
        await using var c = await Open(ct);
        var names = requestIds.Select((_, i) => "@id" + i).ToArray();
        await using var cmd = Command(c, null, "SELECT p.* FROM service_request_photos p INNER JOIN service_requests r ON r.id=p.request_id WHERE r.user_id=@user AND r.id IN (" + string.Join(',', names) + ") ORDER BY p.request_id, p.slot", ("@user", userId.ToString()));
        for (var i = 0; i < requestIds.Count; i++) cmd.Parameters.AddWithValue(names[i], requestIds[i].ToString());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var photos = new List<PhotoRecord>();
        while (await reader.ReadAsync(ct)) photos.Add(new(reader.GetGuid(reader.GetOrdinal("id")), reader.GetGuid(reader.GetOrdinal("request_id")),
            reader.GetString("file_path"), reader.GetByte("slot"), Utc(reader.GetDateTime("created_at"))));
        return photos;
    }
    private static async Task<ServiceRequestRecord> Lock(MySqlConnection c, MySqlTransaction tx, Guid user, Guid id, int version, CancellationToken ct)
    {
        await using var cmd = Command(c, tx, "SELECT * FROM service_requests WHERE id=@id AND user_id=@user FOR UPDATE", ("@id", id.ToString()), ("@user", user.ToString()));
        var current = await ReadOne(cmd, ct) ?? throw new RequestNotFoundException();
        if (current.Version != version) throw new RequestConflictException("Request version has changed. Reload before retrying.");
        return current;
    }
    public async Task<ServiceRequestRecord> UpdateAsync(Guid userId, Guid requestId, CreateServiceRequest request, int version, DateTime now, CancellationToken ct)
    {
        await using var c = await Open(ct); await using var tx = await c.BeginTransactionAsync(ct);
        var current = await Lock(c, tx, userId, requestId, version, ct);
        if (!RequestStateRules.CanEdit(current.Status)) throw new RequestConflictException("Only Submitted requests can be edited.");
        await using var cmd = Command(c, tx, """
            UPDATE service_requests SET property_id=@property, category_id=@category, area_type_id=@area,
            description=@description, urgency=@urgency, preferred_date=@date, preferred_start_time=@start,
            preferred_end_time=@end, updated_at=@now, version=version+1 WHERE id=@id AND user_id=@user AND version=@version
            """, ("@property", request.PropertyId.ToString()), ("@category", request.CategoryId), ("@area", request.AreaTypeId),
            ("@description", request.Description), ("@urgency", request.Urgency.ToString()), ("@date", request.PreferredDate?.ToDateTime(TimeOnly.MinValue)),
            ("@start", request.PreferredStartTime?.ToTimeSpan()), ("@end", request.PreferredEndTime?.ToTimeSpan()), ("@now", now),
            ("@id", requestId.ToString()), ("@user", userId.ToString()), ("@version", version));
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new RequestConflictException("Request version has changed.");
        await tx.CommitAsync(ct);
        return current with { PropertyId = request.PropertyId, CategoryId = request.CategoryId, AreaTypeId = request.AreaTypeId,
            Description = request.Description, Urgency = request.Urgency, PreferredDate = request.PreferredDate,
            PreferredStartTime = request.PreferredStartTime, PreferredEndTime = request.PreferredEndTime, UpdatedAtUtc = now, Version = version + 1 };
    }
    public async Task<ServiceRequestRecord> CancelAsync(Guid userId, Guid requestId, string reason, int version, DateTime now, CancellationToken ct)
    {
        await using var c = await Open(ct); await using var tx = await c.BeginTransactionAsync(ct);
        var current = await Lock(c, tx, userId, requestId, version, ct);
        if (!RequestStateRules.CanCancel(current.Status)) throw new RequestConflictException("This request can no longer be cancelled.");
        await using var cmd = Command(c, tx, "UPDATE service_requests SET status='Cancelled', updated_at=@now, version=version+1 WHERE id=@id AND user_id=@user AND version=@version",
            ("@now", now), ("@id", requestId.ToString()), ("@user", userId.ToString()), ("@version", version));
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new RequestConflictException("Request version has changed.");
        await using var history = Command(c, tx, """
            INSERT INTO service_request_status_history (id, request_id, from_status, to_status, actor_user_id, occurred_at, reason)
            VALUES (@event, @id, @from, 'Cancelled', @user, @now, @reason)
            """, ("@event", Guid.NewGuid().ToString()), ("@id", requestId.ToString()), ("@from", current.Status.ToString()),
            ("@user", userId.ToString()), ("@now", now), ("@reason", reason));
        await history.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
        return current with { Status = RequestStatus.Cancelled, Version = version + 1, UpdatedAtUtc = now };
    }
    public async Task<IReadOnlyList<RequestStatusHistoryDto>> TimelineAsync(Guid userId, Guid requestId, CancellationToken ct)
    {
        await using var c = await Open(ct);
        await using var cmd = Command(c, null, """
            SELECT h.* FROM service_request_status_history h INNER JOIN service_requests r ON r.id=h.request_id
            WHERE r.id=@id AND r.user_id=@user ORDER BY h.occurred_at, (h.from_status IS NOT NULL), h.id
            """, ("@id", requestId.ToString()), ("@user", userId.ToString()));
        await using var reader = await cmd.ExecuteReaderAsync(ct); var items = new List<RequestStatusHistoryDto>();
        while (await reader.ReadAsync(ct)) items.Add(new(reader.GetGuid(reader.GetOrdinal("id")),
            reader.IsDBNull(reader.GetOrdinal("from_status")) ? null : Enum.Parse<RequestStatus>(reader.GetString("from_status")),
            Enum.Parse<RequestStatus>(reader.GetString("to_status")), reader.GetGuid(reader.GetOrdinal("actor_user_id")),
            Utc(reader.GetDateTime("occurred_at")), reader.IsDBNull(reader.GetOrdinal("reason")) ? null : reader.GetString("reason")));
        return items;
    }
    public async Task<int> AttachPhotosAsync(Guid userId, Guid requestId, int version, IReadOnlyList<StoredPhoto> photos, DateTime now, CancellationToken ct)
    {
        await using var c = await Open(ct); await using var tx = await c.BeginTransactionAsync(ct);
        var current = await Lock(c, tx, userId, requestId, version, ct);
        if (!RequestStateRules.CanAddPhotos(current.Status)) throw new RequestConflictException("Photos cannot be added in this status.");
        // The parent row lock serializes all attachments, so two batches cannot exceed eight.
        await using var slots = Command(c, tx, "SELECT slot FROM service_request_photos WHERE request_id=@id", ("@id", requestId.ToString()));
        var used = new HashSet<int>();
        await using (var reader = await slots.ExecuteReaderAsync(ct)) while (await reader.ReadAsync(ct)) used.Add(reader.GetByte(0));
        if (photos.Count is < 1 or > 8 || used.Count + photos.Count > 8) throw new RequestConflictException("A request can contain at most 8 photos.");
        foreach (var photo in photos)
        {
            var slot = Enumerable.Range(0, 8).First(i => !used.Contains(i)); used.Add(slot);
            await using var cmd = Command(c, tx, "INSERT INTO service_request_photos (id, request_id, file_path, slot, created_at) VALUES (@photo, @id, @path, @slot, @now)",
                ("@photo", photo.Id.ToString()), ("@id", requestId.ToString()), ("@path", photo.FilePath), ("@slot", slot), ("@now", now));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await using var update = Command(c, tx, "UPDATE service_requests SET version=version+1, updated_at=@now WHERE id=@id AND user_id=@user AND version=@version",
            ("@id", requestId.ToString()), ("@user", userId.ToString()), ("@version", version), ("@now", now));
        if (await update.ExecuteNonQueryAsync(ct) != 1) throw new RequestConflictException("Request version has changed.");
        await tx.CommitAsync(ct); return version + 1;
    }
}
