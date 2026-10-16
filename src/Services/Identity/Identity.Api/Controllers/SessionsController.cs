using Identity.Application.Abstractions.Persistence;
using Identity.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Identity.Api.Controllers;

[ApiController]
[Authorize]
[AuthRequest]
[Route("api/auth/sessions")]
public sealed class SessionsController(ISessionRepository sessions) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var currentId = User.FindFirst("sid")!.Value;
        var active = await sessions.ListAsync(UserId, cancellationToken);
        return Ok(active.Select(s => new { s.Id, s.CreatedAtUtc, s.ExpiresAtUtc,
            IsCurrent = s.Id.ToString() == currentId }));
    }

    [HttpDelete("{sessionId:guid}")]
    public async Task<IActionResult> Revoke(Guid sessionId, CancellationToken cancellationToken)
    {
        // Ownership is enforced by the UPDATE's user_id predicate; unknown IDs disclose nothing.
        await sessions.RevokeAsync(sessionId, UserId, cancellationToken);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> RevokeAll(CancellationToken cancellationToken)
    {
        await sessions.RevokeAllAsync(UserId, cancellationToken);
        return NoContent();
    }
}
