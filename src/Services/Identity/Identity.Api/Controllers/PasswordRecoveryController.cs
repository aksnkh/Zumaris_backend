using Identity.Api.Security;
using Identity.Application.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.BuildingBlocks.Common.Security;
namespace Identity.Api.Controllers;
public sealed class RecoveryExceptionFilter(ILogger<RecoveryExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext c) { var ex = c.Exception as RecoveryException; if (ex is null) logger.LogError("Password recovery operation failed ({FailureType}).", c.Exception.GetType().Name); int status = ex?.Status ?? 503; c.Result = new ObjectResult(ApiResponseError.Create(status, ex?.Message ?? "Password recovery is temporarily unavailable.", c.HttpContext.TraceIdentifier)) { StatusCode = status }; c.ExceptionHandled = true; }
}
[ApiController, AuthRequest, ResponseErrors, AllowAnonymous, ServiceFilter(typeof(RecoveryExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(16384)]
[Route("api/{side:regex(^(app|tech|admin)$)}")]
public sealed class PasswordRecoveryController(PasswordRecoveryService service) : ControllerBase
{
    private static string Application(string side) => side.ToLowerInvariant() switch { "app" => "App", "tech" => "Tech", "admin" => "Admin", _ => throw new RecoveryException(400, "Invalid application.") };
    private static Response<object?> Result(string message, int status) => new(null) { StatusCode = status, Message = message, Title = "Success", MessageLevel = MessageLevel.Cif };
    [HttpPost("forgot-password"), ProducesResponseType(typeof(Response<object>), 202)]
    public async Task<IActionResult> Forgot(string side, ForgotPasswordRequest input, CancellationToken ct) { await service.Forgot(Application(side), input, ct); return StatusCode(202, Result(PasswordRecoveryService.AcceptedMessage, 202)); }
    [HttpPost("reset-password"), ProducesResponseType(typeof(Response<object>), 200)]
    public async Task<IActionResult> Reset(string side, ResetPasswordRequest input, CancellationToken ct) { var app = Application(side); await service.Reset(app, input, ct); Response.Cookies.Delete(AuthCookies.NameFor(app), new() { HttpOnly = true, Secure = !HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(), SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true }); return Ok(Result("Your password has been reset. Sign in with your new password.", 200)); }
}
