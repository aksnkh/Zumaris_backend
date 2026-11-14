using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zumaris.BuildingBlocks.Common.Passport;
using Zumaris.Maintenance.Application;
namespace Zumaris.Maintenance.Api.Controllers;
[ApiController, Authorize, ServiceFilter(typeof(PassportExceptionFilter)), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/properties/{propertyId:guid}/equipment")]
public sealed class EquipmentController(IEquipmentRepository repo, PassportHttpClient access) : ControllerBase
{
    public static readonly string[] Categories = ["Heating", "Electrical", "Plumbing", "Appliance", "Safety", "Other"];
    public static readonly string[] Statuses = ["Active", "NeedsService", "OutOfService"];
    [HttpGet("options")] public IActionResult Options() => Ok(PassportResponse.Of(new { categories = Categories, statuses = Statuses }));
    [HttpGet] public async Task<IActionResult> List(Guid propertyId, int pageNumber = 1, int pageSize = 20, string? category = null, string? status = null, bool archived = false, CancellationToken ct = default) { new EventQuery(pageNumber, pageSize).Validate(); await access.RequireProperty(propertyId, false, ct); if (category is not null && !Categories.Contains(category) || status is not null && !Statuses.Contains(status)) throw new PassportException(400, "Use supported category and status options."); var r = await repo.List(propertyId, pageNumber, pageSize, category, status, archived, ct); return Ok(PassportResponse.Of(new PassportPage<Equipment>(r.Items, r.Total, pageNumber, pageSize))); }
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid propertyId, Guid id, CancellationToken ct) { await access.RequireProperty(propertyId, false, ct); return Ok(PassportResponse.Of(await Find(propertyId, id, ct))); }
    [HttpPost] public async Task<IActionResult> Create(Guid propertyId, EquipmentInput input, CancellationToken ct) { await access.RequireProperty(propertyId, true, ct); await Validate(propertyId, input, ct); var r = await repo.Save(propertyId, null, input, ct); return StatusCode(201, PassportResponse.Of(r, "Equipment created.", 201)); }
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid propertyId, Guid id, EquipmentInput input, CancellationToken ct) { await access.RequireProperty(propertyId, true, ct); var old = await Find(propertyId, id, ct); if (old.Archived) throw new PassportException(409, "Archived equipment cannot be edited."); await Validate(propertyId, input, ct); try { return Ok(PassportResponse.Of(await repo.Save(propertyId, id, input, ct), "Equipment updated.")); } catch (KeyNotFoundException) { throw new PassportException(409, "Equipment changed or was archived. Reload before retrying."); } }
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Archive(Guid propertyId, Guid id, CancellationToken ct) { await access.RequireProperty(propertyId, true, ct); await Find(propertyId, id, ct); await repo.Archive(propertyId, id, ct); return NoContent(); }
    [HttpGet("events")] public async Task<IActionResult> Events(Guid propertyId, [FromQuery] EventQuery query, CancellationToken ct) { query.Validate(); await access.RequireProperty(propertyId, false, ct); var r = await repo.Events(propertyId, query.PageNumber, query.PageSize, query.From?.UtcDateTime, query.To?.UtcDateTime, query.Type, ct); return Ok(PassportResponse.Of(new PassportPage<PassportEvent>(r.Items.Select(e => new PassportEvent("equipment:" + e.EventId, propertyId, e.OccurredAtUtc, "Equipment", e.Type, e.Title, null, e.EquipmentId)).ToList(), r.Total, query.PageNumber, query.PageSize))); }
    private async Task<Equipment> Find(Guid p, Guid id, CancellationToken ct) => await repo.Get(p, id, ct) ?? throw new PassportException(404, "Equipment not found.");
    private async Task Validate(Guid property, EquipmentInput x, CancellationToken ct)
    {
        PassportResponse.Validate(x); if (!Categories.Contains(x.Category) || !Statuses.Contains(x.Status) || x.WarrantyStartsOn > x.WarrantyExpiresOn || x.InstalledOn > x.WarrantyExpiresOn || x.LastServicedOn > x.NextServiceOn) throw new PassportException(400, "Use valid equipment options and chronological dates.");
        var ids = x.ServiceRequestIds ?? []; if (ids.Count > 20 || ids.Distinct().Count() != ids.Count || ids.Contains(Guid.Empty)) throw new PassportException(400, "Provide up to 20 distinct nonempty related request IDs.");
        foreach (var id in ids) { var r = await access.GetAsync<System.Text.Json.JsonElement>("ServiceRequest", $"api/service-requests/{id}", ct); if (r.GetProperty("request").GetProperty("propertyId").GetGuid() != property) throw new PassportException(400, "Related requests must belong to this property."); }
    }
}
