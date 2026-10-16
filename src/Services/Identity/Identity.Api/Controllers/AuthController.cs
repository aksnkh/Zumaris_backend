using Identity.Application.Abstractions.Persistence;
using Identity.Application.Features.Auth;
using Identity.Application.Features.Auth.Models;
using Identity.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.Identity.Application.DTOs;

namespace Identity.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AuthRequest]
public sealed class AuthController(IAuthService authService, ISessionRepository sessions) : ControllerBase
{
    public const string CookieName = "zumaris_refresh_token";

    [HttpPost("login")]
    public async Task<ActionResult<Response<AuthResponse>>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var (response, refreshToken) = await authService.LoginAsync(request, cancellationToken);
            SetRefreshCookie(refreshToken, response.RefreshTokenExpiresAtUtc);
            return Ok(new Response<AuthResponse>(response) { StatusCode = 200, Title = "Success" });
        }
        catch (UnauthorizedAccessException ex) { return Denied<AuthResponse>(ex.Message); }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<Response<AuthResponse>>> Refresh(CancellationToken cancellationToken)
    {
        try
        {
            var (response, refreshToken) = await authService.RefreshAsync(Request.Cookies[CookieName] ?? "", cancellationToken);
            SetRefreshCookie(refreshToken, response.RefreshTokenExpiresAtUtc);
            return Ok(new Response<AuthResponse>(response) { StatusCode = 200, Title = "Success" });
        }
        catch (UnauthorizedAccessException ex)
        {
            DeleteRefreshCookie();
            return Denied<AuthResponse>(ex.Message);
        }
    }

    // Cookie possession is sufficient for logout, even after the access token has expired.
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(Request.Cookies[CookieName] ?? "", cancellationToken);
        if (Guid.TryParse(User.FindFirst("sub")?.Value, out var userId) &&
            Guid.TryParse(User.FindFirst("sid")?.Value, out var sessionId))
            await sessions.RevokeAsync(sessionId, userId, cancellationToken);
        DeleteRefreshCookie();
        return NoContent();
    }

    [HttpPost("register")]
    public async Task<ActionResult<Response<RegisterResponse>>> Register(RegisterRequest request)
    {
        var result = await authService.RegisterAsync(request);
        var response = new Response<RegisterResponse>(result)
        {
            StatusCode = result.Success ? 200 : 400,
            Title = result.Success ? "Success" : "Registration Failed",
            Message = result.Message
        };
        return result.Success ? Ok(response) : BadRequest(response);
    }

    private UnauthorizedObjectResult Denied<T>(string message) => Unauthorized(new Response<T>
        { StatusCode = 401, Title = "Unauthorized", Message = message });

    private CookieOptions CookieOptions() => new()
    {
        HttpOnly = true,
        Secure = !HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true
    };

    private void SetRefreshCookie(string token, DateTime expiresAtUtc)
    {
        // Remove legacy path-scoped cookies so browsers cannot send two values for the same name.
        DeleteLegacyCookies();
        var options = CookieOptions();
        options.Expires = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc));
        Response.Cookies.Append(CookieName, token, options);
    }

    private void DeleteLegacyCookies()
    {
        foreach (var path in new[] { "/api/auth", "/identity/api/auth" })
        {
            var options = CookieOptions();
            options.Path = path;
            Response.Cookies.Delete(CookieName, options);
        }
    }

    private void DeleteRefreshCookie()
    {
        DeleteLegacyCookies();
        Response.Cookies.Delete(CookieName, CookieOptions());
    }
}
