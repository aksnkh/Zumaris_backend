using Identity.Application.Features.Auth;
using Identity.Application.Features.Auth.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.Identity.Application.DTOs;

namespace Identity.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private const string CookieName = "zumaris_refresh_token";
    private readonly IAuthService _authService;
    private readonly IConfiguration _configuration;

    public AuthController(IAuthService authService, IConfiguration configuration)
    {
        _authService = authService;
        _configuration = configuration;
    }
    
    [HttpPost("login")]
    [ProducesResponseType(typeof(Response<AuthResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<Response<AuthResponse>>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var (authResponse, refreshToken) = await _authService.LoginAsync(request, cancellationToken);

            AppendRefreshTokenCookie(refreshToken);

            var response = new Response<AuthResponse>(authResponse)
            {
                StatusCode = StatusCodes.Status200OK,
                Title = "Success",
                Message = "ورود با موفقیت انجام شد.",
                MessageLevel = MessageLevel.Cif
            };

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = new Response<AuthResponse>()
            {
                StatusCode = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Message = ex.Message,
                MessageLevel = MessageLevel.Cif,
                DevMessage = ex.ToString() // یا پیام فنی مرتبط
            };

            return Unauthorized(errorResponse);
        }
    }


    [HttpPost("refresh")]
    [ProducesResponseType(typeof(Response<AuthResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<Response<AuthResponse>>> Refresh(CancellationToken cancellationToken)
    {
        var rawRefreshToken = Request.Cookies[CookieName];

        try
        {
            var (authResponse, newRefreshToken) = await _authService.RefreshAsync(rawRefreshToken ?? string.Empty, cancellationToken);

            AppendRefreshTokenCookie(newRefreshToken);

            var response = new Response<AuthResponse>(authResponse)
            {
                StatusCode = StatusCodes.Status200OK,
                Title = "Success",
                Message = "توکن با موفقیت تمدید شد.",
                MessageLevel = MessageLevel.Cif
            };

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            RemoveRefreshTokenCookie();

            var errorResponse = new Response<AuthResponse>
            {
                StatusCode = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Message = ex.Message,
                MessageLevel = MessageLevel.Cif,
                DevMessage = ex.ToString()
            };

            return Unauthorized(errorResponse);
        }
    }


    [HttpPost("logout")]
    [ProducesResponseType(typeof(Response<bool?>), StatusCodes.Status401Unauthorized)]
    [Authorize]
    public async Task<ActionResult<Response<bool?>>> Logout(CancellationToken cancellationToken)
    {
        var rawRefreshToken = Request.Cookies[CookieName];

        try
        {
            await _authService.LogoutAsync(rawRefreshToken ?? string.Empty, cancellationToken);
            RemoveRefreshTokenCookie();

            var response = new Response<bool?>
            {
                StatusCode = StatusCodes.Status200OK,
                Title = "Success",
                Message = "خروج با موفقیت انجام شد.",
                MessageLevel = MessageLevel.Cif,
                Payload = true
            };

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            RemoveRefreshTokenCookie();

            var errorResponse = new Response<bool?>
            {
                StatusCode = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Message = ex.Message,
                MessageLevel = MessageLevel.Cif,
                DevMessage = ex.ToString()
            };

            return Unauthorized(errorResponse);
        }
    }

    private void AppendRefreshTokenCookie(string refreshToken)
    {
        var days = _configuration.GetValue<int>("Jwt:RefreshTokenExpirationDays", 7);

        Response.Cookies.Append(CookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = !HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(days),
            Path = "/api/auth"
        });
    }

    private void RemoveRefreshTokenCookie()
    {
        Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/api/auth" });
    }


    [HttpPost("register")]
    [ProducesResponseType(typeof(Response<RegisterResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Response<RegisterResponse>>> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid)
        {
            var validationErrors = string.Join("; ", ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage));

            var validationResponse = new Response<RegisterResponse>
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Title = "Validation Error",
                Message = "اطلاعات ارسالی معتبر نمی‌باشد.",
                MessageLevel = MessageLevel.Cif,
                Parameters = validationErrors
            };

            return BadRequest(validationResponse);
        }

        var result = await _authService.RegisterAsync(request);

        if (!result.Success)
        {
            var errorResponse = new Response<RegisterResponse>
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Title = "Registration Failed",
                Message = result.Message ?? "ثبت‌نام با خطا مواجه شد.",
                MessageLevel = MessageLevel.Cif
            };

            return BadRequest(errorResponse);
        }

        var response = new Response<RegisterResponse>(result)
        {
            StatusCode = StatusCodes.Status200OK,
            Title = "Success",
            Message = "ثبت‌نام با موفقیت انجام شد.",
            MessageLevel = MessageLevel.Cif
        };

        return Ok(response);
    }
}
