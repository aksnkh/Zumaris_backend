using Zumaris.BuildingBlocks.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Identity.Api.Controllers;

[ApiController]
[Authorize]
[DisableRateLimiting]
[Route("api/auth/session")]
public sealed class SessionValidationController(Identity.Application.Abstractions.Persistence.IPlatformAdminRepository administrators) : ControllerBase
{
    [HttpGet("admin-access")]
    [ApplicationAccess("Admin")]
    public async Task<IActionResult> AdminAccess(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return await administrators.IsAdminAsync(Guid.Parse(User.FindFirst("sub")!.Value), ct)
            ? NoContent() : StatusCode(403, new { message = "Administrator access is required." });
    }

    // JWT middleware has already validated signature, claims and the active session.
    [HttpGet]
    public IActionResult ValidateSession()
    {
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }
}
