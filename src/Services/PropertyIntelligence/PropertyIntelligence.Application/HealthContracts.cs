using Zumaris.PropertyIntelligence.Domain;
namespace Zumaris.PropertyIntelligence.Application;
public sealed record HealthEvidence(IReadOnlyList<EquipmentEvidence> Equipment, IReadOnlyList<DocumentEvidence> Documents);
public interface IHealthEvidenceClient { Task<HealthEvidence> Load(Guid property, CancellationToken ct); }
public interface IHealthRepository
{
    Task<AssessmentRecord?> Get(Guid property, CancellationToken ct);
    Task<AssessmentRecord> Save(Guid property, Guid actor, HealthAssessment input, DateTime now, CancellationToken ct);
    Task SaveCalculation(HealthScoreResult result, Guid actor, CancellationToken ct);
}
public sealed class HealthService(IHealthRepository repository, IHealthEvidenceClient evidence, TimeProvider clock)
{
    public async Task<HealthScoreResult> Calculate(Guid property, Guid? saveForActor, CancellationToken ct)
    {
        var assessment = await repository.Get(property, ct);
        var sources = await evidence.Load(property, ct);
        var result = HealthCalculator.Calculate(property, assessment, sources.Equipment, sources.Documents, clock.GetUtcNow().UtcDateTime);
        if (saveForActor.HasValue) await repository.SaveCalculation(result, saveForActor.Value, ct);
        return result;
    }
}
