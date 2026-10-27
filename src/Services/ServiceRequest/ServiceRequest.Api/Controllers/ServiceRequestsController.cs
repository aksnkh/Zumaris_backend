using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.ServiceRequests.Application;

namespace Zumaris.ServiceRequests.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/service-requests")]
public sealed class ServiceRequestsController(IServiceRequestService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(Response<CreatedServiceRequest>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create(CreateServiceRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var userId = Guid.Parse(User.FindFirst("sub")!.Value);
        if (Request.Headers["Idempotency-Key"].Count > 1)
            return BadRequest(new ProblemDetails { Status = 400, Title = "Send one Idempotency-Key value." });
        try
        {
            var result = await service.CreateAsync(userId, request, idempotencyKey, cancellationToken);
            if (result.IsReplay) Response.Headers["Idempotency-Replayed"] = "true";
            // Relative Location preserves /service-request when routed through Gateway.
            return Created($"service-requests/{result.Request.RequestId}", new Response<CreatedServiceRequest>(result.Request)
                { StatusCode = 201, Title = "Created", MessageLevel = MessageLevel.Cif, Message = "Service request submitted successfully." });
        }
        catch (RequestValidationException ex) { return BadRequest(new ValidationProblemDetails(ex.Errors) { Status = 400 }); }
        catch (IdempotencyConflictException ex) { return Conflict(new ProblemDetails { Status = 409, Title = ex.Message }); }
    }
}
