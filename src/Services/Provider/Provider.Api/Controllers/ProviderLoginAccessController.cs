using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.BuildingBlocks.Common.Security;
using Zumaris.Provider.Application;
namespace Zumaris.Provider.Api.Controllers;
[ApiController, InternalApplicationAccess, Authorize(AuthenticationSchemes = "IdentityLoginAccess", Policy = "IdentityLoginAccess")]
[ResponseErrors, ServiceFilter(typeof(VerificationExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/internal/users/{userId:guid}/login-access")]
public sealed class ProviderLoginAccessController(IProviderLoginAccessRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid userId, CancellationToken ct)
    {
        if (User.FindFirst("sub")?.Value != userId.ToString())
            return StatusCode(403, ApiResponseError.Create(403, "This credential cannot inspect another account.", HttpContext.TraceIdentifier));
        var access = await repository.GetAsync(userId, ct);
        if (access is null) return NotFound(ApiResponseError.Create(404, "Provider signup was not found.", HttpContext.TraceIdentifier));
        return Ok(new Response<ProviderLoginAccess>(access) { StatusCode = 200, Title = "Success", Message = "Provider login access retrieved successfully.", MessageLevel = MessageLevel.Cif });
    }
}
