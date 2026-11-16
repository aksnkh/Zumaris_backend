using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zumaris.BuildingBlocks.Common.Passport;
using Zumaris.PropertyIntelligence.Application;
using Zumaris.PropertyIntelligence.Domain;
namespace Zumaris.PropertyIntelligence.Api.Controllers;
[ApiController, Authorize, ServiceFilter(typeof(PassportExceptionFilter)), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/properties/{propertyId:guid}/health-score")]
public sealed class HealthScoreController(PassportHttpClient access, IHealthRepository repository, IHealthEvidenceClient evidence, HealthService service, TimeProvider clock) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirst("sub")!.Value);
    [HttpGet] public async Task<IActionResult> Get(Guid propertyId, CancellationToken ct) { await access.RequireProperty(propertyId, false, ct); return Ok(PassportResponse.Of(await service.Calculate(propertyId, null, ct))); }
    [HttpPost("calculate")] public async Task<IActionResult> Calculate(Guid propertyId, CancellationToken ct) { await access.RequireProperty(propertyId, true, ct); return Ok(PassportResponse.Of(await service.Calculate(propertyId, Actor, ct), "Health score calculated and recorded.")); }
    [HttpGet("assessment")] public async Task<IActionResult> Assessment(Guid propertyId, CancellationToken ct) { await access.RequireProperty(propertyId, false, ct); return Ok(PassportResponse.Of(await repository.Get(propertyId, ct))); }
    [HttpPut("assessment"), RequestSizeLimit(262144)]
    public async Task<IActionResult> Save(Guid propertyId, HealthAssessment input, CancellationToken ct)
    {
        await access.RequireProperty(propertyId, true, ct); var now = clock.GetUtcNow().UtcDateTime;
        var errors = HealthAssessmentRules.Validate(input, DateOnly.FromDateTime(now)); if (errors.Count > 0) throw new PassportException(400, string.Join(" ", errors));
        if ((input.SafetyChecks ?? []).Any(x => x.DocumentId.HasValue) || (input.ServiceNotRequiredEquipmentIds ?? []).Count > 0)
        {
            var sources = await evidence.Load(propertyId, ct);
            if ((input.SafetyChecks ?? []).Any(x => x.DocumentId.HasValue && !sources.Documents.Any(d => d.DocumentId == x.DocumentId)) || (input.ServiceNotRequiredEquipmentIds ?? []).Any(id => !sources.Equipment.Any(e => e.EquipmentId == id)))
                throw new PassportException(400, "Linked documents and service exemptions must reference active records belonging to this property.");
        }
        return Ok(PassportResponse.Of(await repository.Save(propertyId, Actor, input, now, ct), "Self-reported health assessment saved."));
    }
    [HttpGet("options")]
    public async Task<IActionResult> Options(Guid propertyId, CancellationToken ct)
    {
        await access.RequireProperty(propertyId, false, ct);
        return Ok(PassportResponse.Of(new { methodVersion = HealthCalculator.MethodVersion, assessmentSource = "SelfReported", weights = HealthCalculator.Weights, safetyCodes = HealthAssessmentRules.SafetyCodes, applicability = new[] { "Required", "NotApplicable" }, review = new[] { "Unknown", "Satisfactory", "Unsatisfactory" }, severity = new[] { "Low", "Medium", "High", "Critical" }, requiredCoveragePercent = 100 }));
    }
}
