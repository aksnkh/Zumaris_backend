using System.Data;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.ServiceRequests.Application;
using Zumaris.ServiceRequests.Domain;

namespace Zumaris.ServiceRequests.Infrastructure;

public sealed class ServiceRequestAdoNetRepository(IConfiguration configuration) : IServiceRequestRepository
{
    private readonly string connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is required.");

    public async Task<PersistedRequest?> FindByKeyAsync(Guid userId, string key, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("SELECT * FROM service_requests WHERE user_id = @user AND idempotency_key = @key", connection);
        command.Parameters.AddWithValue("@user", userId.ToString());
        command.Parameters.AddWithValue("@key", key);
        await using var r = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;
        var record = ReadRecord(r);
        return new PersistedRequest(record, r.GetString("request_hash"));
    }

    public async Task<CreateResult> CreateAsync(ServiceRequestRecord request, string? key, string requestHash,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var insert = new MySqlCommand("""
                INSERT INTO service_requests
                (id, user_id, property_id, category_id, area_type_id, description, urgency,
                 preferred_date, preferred_start_time, preferred_end_time, time_zone_id,
                 status, technician_id, created_at, updated_at, version, idempotency_key, request_hash)
                VALUES (@id, @user, @property, @category, @area, @description, @urgency,
                 @date, @start, @end, @zone, @status, NULL, @created, NULL, 1, @key, @hash)
                """, connection, transaction);
            foreach (var (name, value) in new (string, object?)[]
            {
                ("@id", request.RequestId.ToString()), ("@user", request.UserId.ToString()),
                ("@property", request.PropertyId.ToString()), ("@category", request.CategoryId.ToString()),
                ("@area", request.AreaTypeId.ToString()), ("@description", request.Description),
                ("@urgency", request.Urgency.ToString()), ("@date", request.PreferredDate?.ToDateTime(TimeOnly.MinValue)),
                ("@start", request.PreferredStartTime?.ToTimeSpan()), ("@end", request.PreferredEndTime?.ToTimeSpan()),
                ("@zone", request.TimeZoneId), ("@status", request.Status.ToString()), ("@created", request.CreatedAtUtc),
                ("@key", key), ("@hash", requestHash)
            }) insert.Parameters.AddWithValue(name, value ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await using var history = new MySqlCommand("""
                INSERT INTO service_request_status_history
                (id, request_id, from_status, to_status, actor_user_id, occurred_at, reason)
                VALUES (@id, @request, NULL, 'Submitted', @user, @created, NULL)
                """, connection, transaction);
            history.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            history.Parameters.AddWithValue("@request", request.RequestId.ToString());
            history.Parameters.AddWithValue("@user", request.UserId.ToString());
            history.Parameters.AddWithValue("@created", request.CreatedAtUtc);
            await history.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CreateResult(ServiceRequestService.ToCreated(request), false);
        }
        catch (MySqlException ex) when (ex.Number == 1062 && key is not null)
        {
            // Transaction has rolled back before looking up the concurrent winner.
            var existing = await FindByKeyAsync(request.UserId, key, cancellationToken);
            if (existing is null) throw;
            if (existing.RequestHash != requestHash) throw new IdempotencyConflictException();
            return new CreateResult(ServiceRequestService.ToCreated(existing.Request), true);
        }
    }
    internal static ServiceRequestRecord ReadRecord(MySqlDataReader r)
    {
        return new ServiceRequestRecord(Guid.Parse(r.GetString("id")), Guid.Parse(r.GetString("user_id")),
            Guid.Parse(r.GetString("property_id")), Guid.Parse(r.GetString("category_id")), Guid.Parse(r.GetString("area_type_id")),
            r.GetString("description"), Enum.Parse<RequestUrgency>(r.GetString("urgency")),
            r.IsDBNull(r.GetOrdinal("preferred_date")) ? null : DateOnly.FromDateTime(r.GetDateTime("preferred_date")),
            r.IsDBNull(r.GetOrdinal("preferred_start_time")) ? null : TimeOnly.FromTimeSpan(r.GetTimeSpan("preferred_start_time")),
            r.IsDBNull(r.GetOrdinal("preferred_end_time")) ? null : TimeOnly.FromTimeSpan(r.GetTimeSpan("preferred_end_time")),
            r.GetString("time_zone_id"), Enum.Parse<RequestStatus>(r.GetString("status")),
            r.IsDBNull(r.GetOrdinal("technician_id")) ? null : Guid.Parse(r.GetString("technician_id")),
            DateTime.SpecifyKind(r.GetDateTime("created_at"), DateTimeKind.Utc),
            r.IsDBNull(r.GetOrdinal("updated_at")) ? null : DateTime.SpecifyKind(r.GetDateTime("updated_at"), DateTimeKind.Utc), r.GetInt32("version"));
    }
}
