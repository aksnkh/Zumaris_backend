using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zumaris.ServiceCatalog.Application.Abstractions;
using Zumaris.ServiceCatalog.Domain.Entities;
namespace Zumaris.ServiceCatalog.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/areas")]
public sealed class AreasController(IAreaRepository areas) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<Area>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<Area>>> Get(CancellationToken cancellationToken) =>
        Ok(await areas.ListAsync(cancellationToken));
}
