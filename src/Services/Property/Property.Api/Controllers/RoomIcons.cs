using Zumaris.BuildingBlocks.Common.Passport;
using Zumaris.Property.Application.Features;

namespace Zumaris.Property.Api.Controllers;

public sealed class RoomIcons(PassportHttpClient catalog, ILogger<RoomIcons> logger)
{
    // Stable area IDs agreed in database/service-catalog/001_areas.sql.
    // Room names are user data; map the supported room TYPE instead.
    private static readonly IReadOnlyDictionary<string, int> AreaIds = new Dictionary<string, int>
    {
        ["Kitchen"] = 1, ["Bathroom"] = 2, ["Utility"] = 4,
        ["LivingRoom"] = 5, ["DiningRoom"] = 6, ["Bedroom"] = 7,
        ["Hallway"] = 8, ["Garage"] = 11, ["Garden"] = 12, ["Other"] = 19
    };

    public sealed record AreaIcon(int Id, string? IconUrl);

    public async Task<IReadOnlyList<PropertyRoom>> Enrich(IReadOnlyList<PropertyRoom> rooms, CancellationToken ct)
    {
        if (rooms.Count == 0) return rooms;
        IReadOnlyList<AreaIcon> areas;
        try
        {
            // One authenticated request for the whole page, never one request per room.
            areas = await catalog.GetAsync<IReadOnlyList<AreaIcon>>("ServiceCatalog", "api/areas", ct);
        }
        catch (PassportException ex) when (ex.Status is 503 or 404)
        {
            ct.ThrowIfCancellationRequested();
            logger.LogWarning("Room icons are unavailable from ServiceCatalog ({Status}).", ex.Status);
            return rooms.Select(room => room with { IconUrl = null }).ToArray();
        }
        return rooms.Select(room => room with
        {
            IconUrl = AreaIds.TryGetValue(room.Details.Type, out var areaId)
                ? areas.FirstOrDefault(area => area.Id == areaId)?.IconUrl : null
        }).ToArray();
    }
}
