using System.Data;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.ServiceRequests.Application;

namespace Zumaris.ServiceRequests.Infrastructure;

public sealed class ServiceHistoryAdoNetRepository(IConfiguration configuration) : IServiceHistoryRepository
{
    private readonly string connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection is required.");
    private const string OwnerFilter = "r.user_id=@user";
    private static string? Text(MySqlDataReader r, string name) => r.IsDBNull(r.GetOrdinal(name)) ? null : r.GetString(name);
    private static DateTime? Date(MySqlDataReader r, string name) => r.IsDBNull(r.GetOrdinal(name)) ? null
        : DateTime.SpecifyKind(r.GetDateTime(name), DateTimeKind.Utc);
    private static decimal? Amount(MySqlDataReader r) => r.IsDBNull(r.GetOrdinal("total_amount")) ? null : r.GetDecimal("total_amount");
    private static string Status(MySqlDataReader r) => r.GetString("status") is "Submitted" ? "Pending" : r.GetString("status");
    private static MySqlCommand Command(MySqlConnection c, MySqlTransaction? tx, string sql, Guid user)
    {
        var cmd = new MySqlCommand(sql, c, tx);
        cmd.Parameters.AddWithValue("@user", user.ToString()); return cmd;
    }
    public async Task<PagedResult<ServiceHistoryDto>> GetHistoryAsync(Guid userId, HistoryFilterRequest request, CancellationToken ct)
    {
        // Only validated, fixed SQL fragments are selected; all values remain parameters.
        var filter = request.Status switch
        {
            null => "", "Paid" => " AND r.payment_status='Paid'",
            "Completed" or "Cancelled" => " AND r.status=@status",
            _ => throw new ArgumentException("History filter must be normalized.")
        };
        await using var c = new MySqlConnection(connectionString); await c.OpenAsync(ct);
        // Count and page share a consistent snapshot even if requests change concurrently.
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using var count = Command(c, tx, $"SELECT COUNT(*) FROM service_requests r WHERE {OwnerFilter}{filter}", userId);
        count.Parameters.AddWithValue("@status", request.Status);
        var total = Convert.ToInt64(await count.ExecuteScalarAsync(ct));
        await using var cmd = Command(c, tx, $"""
            SELECT r.* FROM service_requests r WHERE {OwnerFilter}{filter}
            ORDER BY COALESCE(r.service_date,r.created_at) DESC, r.id DESC LIMIT @size OFFSET @offset
            """, userId);
        cmd.Parameters.AddWithValue("@status", request.Status);
        cmd.Parameters.AddWithValue("@size", request.PageSize);
        cmd.Parameters.AddWithValue("@offset", ((long)request.PageNumber - 1) * request.PageSize);
        var items = new List<ServiceHistoryDto>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                items.Add(new(r.GetGuid(r.GetOrdinal("id")), r.GetString("request_number"), Text(r,"service_name"),
                    Text(r,"category_name"), Text(r,"category_icon"), Text(r,"service_area"), Text(r,"technician_name"),
                    Date(r,"service_date"), Status(r), r.GetString("payment_status"), Amount(r), Text(r,"currency_code"), Date(r,"created_at")!.Value) { CategoryId = r.GetInt32("category_id") });
        await tx.CommitAsync(ct);
        return new(items, request.PageNumber, request.PageSize, total);
    }
    public async Task<ServiceHistoryDetailDto?> GetByIdAsync(Guid userId, Guid historyId, CancellationToken ct)
    {
        await using var c = new MySqlConnection(connectionString); await c.OpenAsync(ct);
        await using var cmd = Command(c, null, $"""
            SELECT r.*,
              (SELECT MAX(h.occurred_at) FROM service_request_status_history h WHERE h.request_id=r.id AND h.to_status='Completed') AS completed_at,
              (SELECT h.occurred_at FROM service_request_status_history h WHERE h.request_id=r.id AND h.to_status='Cancelled' ORDER BY h.occurred_at DESC,h.id DESC LIMIT 1) AS cancelled_at,
              (SELECT h.reason FROM service_request_status_history h WHERE h.request_id=r.id AND h.to_status='Cancelled' ORDER BY h.occurred_at DESC,h.id DESC LIMIT 1) AS cancellation_reason
            FROM service_requests r WHERE {OwnerFilter} AND r.id=@id
            """, userId);
        cmd.Parameters.AddWithValue("@id", historyId.ToString());
        await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
        if (!await r.ReadAsync(ct)) return null;
        var id = r.GetGuid(r.GetOrdinal("id"));
        return new(id, id, r.GetString("request_number"), r.GetGuid(r.GetOrdinal("user_id")), r.GetGuid(r.GetOrdinal("property_id")),
            Text(r,"property_address"), r.GetInt32("category_id"), Text(r,"category_name"), Text(r,"service_name"),
            Text(r,"service_area"), Text(r,"description"), r.IsDBNull(r.GetOrdinal("technician_id")) ? null : r.GetGuid(r.GetOrdinal("technician_id")),
            Text(r,"technician_name"), Date(r,"service_date"), Date(r,"completed_at"), Date(r,"cancelled_at"), Text(r,"cancellation_reason"),
            Status(r), r.GetString("payment_status"), Amount(r), Text(r,"currency_code"), Date(r,"created_at")!.Value, Date(r,"updated_at"));
    }
}
