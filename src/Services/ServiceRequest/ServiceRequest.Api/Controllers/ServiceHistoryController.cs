using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zumaris.ServiceRequests.Application;

namespace Zumaris.ServiceRequests.Api.Controllers;

[ApiController]
[Authorize]
[ServiceFilter(typeof(ManagementExceptionFilter))]
[Route("api/service-history")]
public sealed class ServiceHistoryController(IServiceHistoryService service) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ServiceHistoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? status = null, [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.GetHistoryAsync(UserId, new(status, pageNumber, pageSize), ct));
    }
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ServiceHistoryDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var item = await service.GetByIdAsync(UserId, id, ct);
        return item is null ? NotFound() : Ok(item);
    }
}
