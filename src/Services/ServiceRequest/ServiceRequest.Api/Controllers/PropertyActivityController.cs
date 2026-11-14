using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zumaris.BuildingBlocks.Common.Passport;
using Zumaris.ServiceRequests.Application;
namespace Zumaris.ServiceRequests.Api.Controllers;
[ApiController, Authorize, ServiceFilter(typeof(PassportExceptionFilter)), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/properties/{propertyId:guid}")]
public sealed class PropertyActivityController(IPropertyActivityRepository repo, PassportHttpClient access) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirst("sub")!.Value);
    [HttpGet("service-summary")] public async Task<IActionResult> Summary(Guid propertyId, CancellationToken ct) { await access.RequireProperty(propertyId, false, ct); return Ok(PassportResponse.Of(await repo.Summary(Actor, propertyId, ct))); }
    [HttpGet("service-events")] public async Task<IActionResult> Events(Guid propertyId, [FromQuery] EventQuery query, CancellationToken ct) { query.Validate(); await access.RequireProperty(propertyId, false, ct); var r = await repo.Events(Actor, propertyId, query.PageNumber, query.PageSize, query.From?.UtcDateTime, query.To?.UtcDateTime, query.Type, ct); return Ok(PassportResponse.Of(new PassportPage<PassportEvent>(r.Items.Select(e => new PassportEvent("service:" + e.EventId, propertyId, e.OccurredAtUtc, "ServiceRequest", "Request" + e.Status, e.RequestNumber + ": " + e.Status, e.Reason, e.RequestId)).ToList(), r.Total, query.PageNumber, query.PageSize))); }
}
