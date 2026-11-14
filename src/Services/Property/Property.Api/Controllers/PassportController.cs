using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Zumaris.BuildingBlocks.Common.Passport;
using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.Property.Application.Abstractions;
using Zumaris.Property.Application.Features;
namespace Zumaris.Property.Api.Controllers;
[ApiController, Authorize, ServiceFilter(typeof(PassportExceptionFilter)), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/properties/{propertyId:guid}")]
public sealed class PassportController(IPropertyRepository properties, IPassportRepository repo, PassportHttpClient http) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirst("sub")!.Value);
    private static readonly string[] RoomTypes = ["Bedroom", "Bathroom", "Kitchen", "LivingRoom", "DiningRoom", "Hallway", "Utility", "Garage", "Garden", "Other"];
    private async Task<Zumaris.Property.Domain.Entities.PropertyAccess> Access(Guid p, bool write, CancellationToken ct) { var a = await properties.GetAsync(Actor, p, ct) ?? throw new PassportException(404, "Property not found."); if (write && a.Property.CreatedByUserId != Actor) throw new PassportException(403, "Only the property creator can modify Passport records."); return a; }
    [HttpGet("access")] public async Task<IActionResult> Permission(Guid propertyId, CancellationToken ct) { var a = await Access(propertyId, false, ct); return Ok(PassportResponse.Of(new PropertyPermission(propertyId, a.Property.CreatedByUserId == Actor))); }
    [HttpGet("passport/profile")] public async Task<IActionResult> Profile(Guid propertyId, CancellationToken ct) { await Access(propertyId, false, ct); return Ok(PassportResponse.Of(await repo.Profile(propertyId, ct))); }
    [HttpPut("passport/profile")] public async Task<IActionResult> Profile(Guid propertyId, PassportProfile input, CancellationToken ct) { await Access(propertyId, true, ct); Area(input.FloorAreaSquareMetres); await repo.SetProfile(propertyId, input, ct); return Ok(PassportResponse.Of(await repo.Profile(propertyId, ct), "Passport profile updated.")); }
    [HttpGet("rooms/options")] public IActionResult Options() => Ok(PassportResponse.Of(new { types = RoomTypes, areaUnit = "m2" }));
    [HttpGet("rooms")] public async Task<IActionResult> Rooms(Guid propertyId, int pageNumber = 1, int pageSize = 20, bool archived = false, CancellationToken ct = default) { new EventQuery(pageNumber, pageSize).Validate(); await Access(propertyId, false, ct); var r = await repo.Rooms(propertyId, pageNumber, pageSize, archived, ct); return Ok(PassportResponse.Of(new PassportPage<PropertyRoom>(r.Items, r.Total, pageNumber, pageSize))); }
    [HttpGet("rooms/{id:guid}")] public async Task<IActionResult> Room(Guid propertyId, Guid id, CancellationToken ct) { await Access(propertyId, false, ct); return Ok(PassportResponse.Of(await FindRoom(propertyId, id, ct))); }
    [HttpPost("rooms")] public async Task<IActionResult> CreateRoom(Guid propertyId, RoomInput input, CancellationToken ct) { await Access(propertyId, true, ct); ValidateRoom(input); return StatusCode(201, PassportResponse.Of(await repo.SaveRoom(propertyId, null, input, ct), "Room created.", 201)); }
    [HttpPut("rooms/{id:guid}")] public async Task<IActionResult> UpdateRoom(Guid propertyId, Guid id, RoomInput input, CancellationToken ct) { await Access(propertyId, true, ct); if ((await FindRoom(propertyId, id, ct)).Archived) throw new PassportException(409, "Archived rooms cannot be edited."); ValidateRoom(input); try { return Ok(PassportResponse.Of(await repo.SaveRoom(propertyId, id, input, ct), "Room updated.")); } catch (KeyNotFoundException) { throw new PassportException(409, "Room changed or was archived. Reload before retrying."); } }
    [HttpDelete("rooms/{id:guid}")] public async Task<IActionResult> ArchiveRoom(Guid propertyId, Guid id, CancellationToken ct) { await Access(propertyId, true, ct); await FindRoom(propertyId, id, ct); await repo.ArchiveRoom(propertyId, id, ct); return NoContent(); }
    [HttpGet("events")] public async Task<IActionResult> Events(Guid propertyId, [FromQuery] EventQuery query, CancellationToken ct) { query.Validate(); await Access(propertyId, false, ct); var r = await repo.Events(propertyId, query.PageNumber, query.PageSize, query.From?.UtcDateTime, query.To?.UtcDateTime, ct); return Ok(PassportResponse.Of(new PassportPage<ManualPropertyEvent>(r.Items, r.Total, query.PageNumber, query.PageSize))); }
    [HttpGet("events/{id:guid}")] public async Task<IActionResult> Event(Guid propertyId, Guid id, CancellationToken ct) { await Access(propertyId, false, ct); return Ok(PassportResponse.Of(await FindEvent(propertyId, id, ct))); }
    [HttpPost("events")] public async Task<IActionResult> CreateEvent(Guid propertyId, ManualEventInput input, CancellationToken ct) { await Access(propertyId, true, ct); ValidateEvent(input); return StatusCode(201, PassportResponse.Of(await repo.SaveEvent(propertyId, null, input, ct), "Property event created.", 201)); }
    [HttpPut("events/{id:guid}")] public async Task<IActionResult> UpdateEvent(Guid propertyId, Guid id, ManualEventInput input, CancellationToken ct) { await Access(propertyId, true, ct); if ((await FindEvent(propertyId, id, ct)).Archived) throw new PassportException(409, "Archived events cannot be edited."); ValidateEvent(input); try { return Ok(PassportResponse.Of(await repo.SaveEvent(propertyId, id, input, ct), "Property event updated.")); } catch (KeyNotFoundException) { throw new PassportException(409, "Event changed or was archived. Reload before retrying."); } }
    [HttpDelete("events/{id:guid}")] public async Task<IActionResult> ArchiveEvent(Guid propertyId, Guid id, CancellationToken ct) { await Access(propertyId, true, ct); await FindEvent(propertyId, id, ct); await repo.ArchiveEvent(propertyId, id, ct); return NoContent(); }
    [HttpGet("passport/metrics")]
    public async Task<IActionResult> Metrics(Guid propertyId, CancellationToken ct)
    {
        await Access(propertyId, false, ct);
        var eq = http.GetAsync<Response<JsonElement>>("Maintenance", $"api/properties/{propertyId}/equipment?pageSize=1", ct);
        var doc = http.GetAsync<Response<JsonElement>>("Document", $"api/properties/{propertyId}/documents?pageSize=1", ct);
        var service = http.GetAsync<Response<JsonElement>>("ServiceRequest", $"api/properties/{propertyId}/service-summary", ct);
        await Task.WhenAll(eq, doc, service); var rooms = await repo.Rooms(propertyId, 1, 1, false, ct); var profile = await repo.Profile(propertyId, ct);
        return Ok(PassportResponse.Of(new { propertyId, healthScore = (int?)null, healthScoreStatus = "NotConfigured", equipmentCount = eq.Result.Payload.GetProperty("totalCount").GetInt64(), documentCount = doc.Result.Payload.GetProperty("totalCount").GetInt64(), roomCount = rooms.Total, profile.FloorAreaSquareMetres, serviceSummary = service.Result.Payload }));
    }
    [HttpGet("timeline")]
    public async Task<IActionResult> Timeline(Guid propertyId, [FromQuery] EventQuery query, string? source = null, CancellationToken ct = default)
    {
        query.Validate(); await Access(propertyId, false, ct); if (source is not null && source is not ("Manual" or "Equipment" or "Document" or "ServiceRequest")) throw new PassportException(400, "Use Manual, Equipment, Document or ServiceRequest as source.");
        var all = new List<PassportEvent>(); long total = 0; int needed = query.PageNumber * query.PageSize;
        if (source is null or "Manual")
        {
            if (query.Type is null or "ManualEvent") for (int page = 1; page <= 100; page++) { var m = await repo.Events(propertyId, page, 100, query.From?.UtcDateTime, query.To?.UtcDateTime, ct); if (page == 1) total += m.Total; all.AddRange(m.Items.Select(x => new PassportEvent("manual:" + x.EventId, propertyId, x.Details.EventAt.UtcDateTime, "Manual", "ManualEvent", x.Details.Title, x.Details.Description, x.EventId, x.Details.Cost, x.Details.CurrencyCode))); if (page * 100 >= needed || page * 100 >= m.Total) break; }
        }
        foreach (var service in new[] { "Maintenance", "Document", "ServiceRequest" })
        {
            string name = service == "Maintenance" ? "Equipment" : service;
            if (source is not null && source != name) continue;
            string suffix = service switch { "Maintenance" => "equipment/events", "Document" => "documents/events", _ => "service-events" };
            for (int page = 1; page <= 100; page++)
            {
                var path = $"api/properties/{propertyId}/{suffix}?pageNumber={page}&pageSize=100";
                if (query.From.HasValue) path += "&from=" + Uri.EscapeDataString(query.From.Value.ToString("O")); if (query.To.HasValue) path += "&to=" + Uri.EscapeDataString(query.To.Value.ToString("O")); if (query.Type is not null) path += "&type=" + Uri.EscapeDataString(query.Type);
                var r = await http.GetAsync<Response<PassportPage<PassportEvent>>>(service, path, ct); if (page == 1) total += r.Payload.TotalCount; all.AddRange(r.Payload.Items); if (page * 100 >= needed || page * 100 >= r.Payload.TotalCount) break;
            }
        }
        var items = all.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.EventId, StringComparer.Ordinal).Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToList();
        return Ok(PassportResponse.Of(new PassportPage<PassportEvent>(items, total, query.PageNumber, query.PageSize)));
    }
    private async Task<PropertyRoom> FindRoom(Guid p, Guid id, CancellationToken ct) => await repo.Room(p, id, ct) ?? throw new PassportException(404, "Room not found.");
    private async Task<ManualPropertyEvent> FindEvent(Guid p, Guid id, CancellationToken ct) => await repo.Event(p, id, ct) ?? throw new PassportException(404, "Property event not found.");
    private static void Area(decimal? a) { if (a is <= 0 or > 1000000 || a.HasValue && decimal.Round(a.Value, 2) != a.Value) throw new PassportException(400, "Floor area must be greater than zero and no more than 1000000 square metres with up to two decimal places, or null."); }
    private static void ValidateRoom(RoomInput x) { PassportResponse.Validate(x); Area(x.FloorAreaSquareMetres); if (!RoomTypes.Contains(x.Type)) throw new PassportException(400, "Use a supported room type."); }
    private static void ValidateEvent(ManualEventInput x) { PassportResponse.Validate(x); if (x.EventAt == default || x.EventAt.Year < 1900 || x.EventAt.Year > 9998 || x.Cost < 0 || x.Cost > 9999999999.99m || x.Cost.HasValue && decimal.Round(x.Cost.Value, 2) != x.Cost.Value || x.Cost.HasValue != (x.CurrencyCode is not null) || x.CurrencyCode is not null && (x.CurrencyCode.Length != 3 || !x.CurrencyCode.All(c => c is >= 'A' and <= 'Z'))) throw new PassportException(400, "Provide a valid event date and an optional nonnegative cost with a three-letter uppercase currency code."); }
}
