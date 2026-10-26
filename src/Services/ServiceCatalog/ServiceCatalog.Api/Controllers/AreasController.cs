using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zumaris.ServiceCatalog.Application.Abstractions;
namespace Zumaris.ServiceCatalog.Api.Controllers;

public sealed record AreaResponse(int Id, string Name, string? IconUrl);

[ApiController]
[Authorize]
[Route("api/areas")]
public sealed class AreasController(IAreaRepository areas, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AreaResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AreaResponse>>> Get(CancellationToken cancellationToken)
    {
        var prefix = (configuration["AreaIcons:PublicPath"] ?? "/service-catalog/api/areas/icons").TrimEnd('/');
        return Ok((await areas.ListAsync(cancellationToken)).Select(area =>
            new AreaResponse(area.Id, area.Name, CatalogIconFiles.IsValidName(area.Icon)
                ? prefix + "/" + Uri.EscapeDataString(area.Icon!) : null)).ToArray());
    }
}
