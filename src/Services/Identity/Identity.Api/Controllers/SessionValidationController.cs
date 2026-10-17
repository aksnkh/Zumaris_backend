using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Identity.Api.Controllers;

[ApiController]
[Authorize]
[DisableRateLimiting]
[Route("api/auth/session")]
public sealed class SessionValidationController : ControllerBase
{
    // JWT middleware has already validated signature, claims and the active session.
    [HttpGet]
    public IActionResult ValidateSession()
    {
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }
}
