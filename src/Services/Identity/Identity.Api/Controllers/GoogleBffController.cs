using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Identity.Application.Features.Auth;
using Identity.Application.Features.Auth.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Identity.Api.Controllers;

public sealed record GoogleLoginRequest([Required, StringLength(16384)] string IdToken);
public sealed record BffRefreshRequest([Required, StringLength(128)] string RefreshToken);
public sealed record BffTokenResponse(AuthResponse Payload, string RefreshToken);

// Server-to-server credential; never put it in NEXT_PUBLIC_ variables or browser JavaScript.
public sealed class BffOnlyFilter(IConfiguration configuration) : IAuthorizationFilter, IExceptionFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
        var expected = configuration["Bff:ClientKey"] ?? "";
        if (Encoding.UTF8.GetByteCount(expected) < 32)
        {
            context.Result = new ObjectResult(new ProblemDetails { Status = 503, Title = "BFF authentication is not configured." }) { StatusCode = 503 };
            return;
        }
        var supplied = context.HttpContext.Request.Headers["X-Zumaris-Bff-Key"];
        var provided = supplied.FirstOrDefault() ?? "";
        if (supplied.Count != 1 || provided.Length > 512 || !CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided)))
            context.Result = new UnauthorizedResult();
    }
    public void OnException(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            UnauthorizedAccessException => new UnauthorizedObjectResult(new ProblemDetails { Status = 401, Title = "Invalid identity or session." }),
            ExternalLoginConflictException => new ConflictObjectResult(new ProblemDetails { Status = 409, Title = "Sign in to your existing account and explicitly link Google." }),
            GoogleLoginUnavailableException => new ObjectResult(new ProblemDetails { Status = 503, Title = "Google sign-in is unavailable or not configured." }) { StatusCode = 503 },
            _ => null
        };
        if (context.Result is not null) context.ExceptionHandled = true;
    }
}

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[ServiceFilter(typeof(BffOnlyFilter))]
[RequestSizeLimit(32768)]
[Route("api/auth")]
public sealed class GoogleBffController(GoogleAuthService google, IAuthService authService) : ControllerBase
{
    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin(GoogleLoginRequest request, CancellationToken ct)
    {
        var (response, refresh) = await google.LoginAsync(request.IdToken, ct);
        return Ok(new BffTokenResponse(response, refresh));
    }
    [Authorize]
    [HttpPost("google/link")]
    public async Task<IActionResult> Link(GoogleLoginRequest request, CancellationToken ct)
    {
        await google.LinkAsync(Guid.Parse(User.FindFirst("sub")!.Value), request.IdToken, ct);
        return NoContent();
    }
    [HttpPost("bff/refresh")]
    public async Task<IActionResult> Refresh(BffRefreshRequest request, CancellationToken ct)
    {
        var (response, refresh) = await authService.RefreshAsync(request.RefreshToken, ct);
        return Ok(new BffTokenResponse(response, refresh));
    }
    [HttpPost("bff/logout")]
    public async Task<IActionResult> Logout(BffRefreshRequest request, CancellationToken ct)
    {
        await authService.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }
}
