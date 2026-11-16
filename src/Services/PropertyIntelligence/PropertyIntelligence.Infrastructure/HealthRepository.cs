using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.PropertyIntelligence.Application;
using Zumaris.PropertyIntelligence.Domain;
namespace Zumaris.PropertyIntelligence.Infrastructure;
public sealed class HealthRepository(IConfiguration config) : IHealthRepository
{
    private async Task<MySqlConnection> Open(CancellationToken ct) { var c = new MySqlConnection(config.GetConnectionString("DefaultConnection")); try { await c.OpenAsync(ct); return c; } catch { await c.DisposeAsync(); throw; } }
    private static MySqlCommand Cmd(MySqlConnection c, string sql, params (string, object?)[] values) { var q = new MySqlCommand(sql, c); foreach (var (k, v) in values) q.Parameters.AddWithValue(k, v ?? DBNull.Value); return q; }
    public async Task<AssessmentRecord?> Get(Guid property, CancellationToken ct)
    {
        await using var c = await Open(ct); await using var q = Cmd(c, "SELECT assessment,updated_by_user_id,updated_at FROM health_assessments WHERE property_id=@p", ("@p", property.ToString())); await using var r = await q.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new(property, JsonSerializer.Deserialize<HealthAssessment>(r.GetString(0))!, r.GetGuid(1), DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc)) : null;
    }
    public async Task<AssessmentRecord> Save(Guid property, Guid actor, HealthAssessment input, DateTime now, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(input); await using var c = await Open(ct); await using var tx = await c.BeginTransactionAsync(ct);
        await using var q = Cmd(c, "INSERT INTO health_assessments(property_id,assessment,updated_by_user_id,updated_at)VALUES(@p,@json,@actor,@now) ON DUPLICATE KEY UPDATE assessment=@json,updated_by_user_id=@actor,updated_at=@now", ("@p", property.ToString()), ("@json", json), ("@actor", actor.ToString()), ("@now", now)); q.Transaction = tx; await q.ExecuteNonQueryAsync(ct);
        await using var audit = Cmd(c, "INSERT INTO health_assessment_history(id,property_id,assessment,actor_user_id,occurred_at)VALUES(@id,@p,@json,@actor,@now)", ("@id", Guid.NewGuid().ToString()), ("@p", property.ToString()), ("@json", json), ("@actor", actor.ToString()), ("@now", now)); audit.Transaction = tx; await audit.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
        return new(property, input, actor, now);
    }
    public async Task SaveCalculation(HealthScoreResult result, Guid actor, CancellationToken ct)
    {
        await using var c = await Open(ct); await using var q = Cmd(c, "INSERT INTO health_score_calculations(id,property_id,method_version,result,actor_user_id,calculated_at)VALUES(@id,@p,@version,@json,@actor,@now)", ("@id", Guid.NewGuid().ToString()), ("@p", result.PropertyId.ToString()), ("@version", result.MethodVersion), ("@json", JsonSerializer.Serialize(result)), ("@actor", actor.ToString()), ("@now", result.CalculatedAtUtc)); await q.ExecuteNonQueryAsync(ct);
    }
}
