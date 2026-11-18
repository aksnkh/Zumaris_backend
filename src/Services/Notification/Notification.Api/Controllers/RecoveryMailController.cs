using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.BuildingBlocks.Common.Security;
using Zumaris.Notification.Application;
namespace Zumaris.Notification.Api.Controllers;
public sealed class MailFailureFilter(ILogger<MailFailureFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext c) { logger.LogWarning("Recovery mail delivery failed ({FailureType}).", c.Exception.GetType().Name); c.Result = new ObjectResult(ApiResponseError.Create(503, "Email delivery is temporarily unavailable.", c.HttpContext.TraceIdentifier)) { StatusCode = 503 }; c.ExceptionHandled = true; }
}
[ApiController, Authorize(Policy = "RecoveryMail"), ResponseErrors, ServiceFilter(typeof(MailFailureFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(8192)]
[Route("api/internal/password-recovery-email")]
public sealed class RecoveryMailController(IRecoveryMailSender sender, IConfiguration config) : ControllerBase
{
    private IActionResult Invalid() => BadRequest(ApiResponseError.Create(400, "Invalid password recovery delivery.", HttpContext.TraceIdentifier));
    [HttpPost]
    public async Task<IActionResult> Send(RecoveryMail input, CancellationToken ct)
    {
        if (input.Application is not ("App" or "Tech" or "Admin") || input.Application != User.FindFirst("application")?.Value || input.DeliveryId == Guid.Empty || input.DeliveryId.ToString() != User.FindFirst("delivery_id")?.Value || !MailAddress.TryCreate(input.Recipient, out var recipient) || recipient.Address != input.Recipient || input.Recipient.Length > 254 || input.ExpiresAtUtc.Kind != DateTimeKind.Utc || input.ExpiresAtUtc <= DateTime.UtcNow || input.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(30)) return Invalid();
        var expectedHash = User.FindFirst("email_hash")?.Value ?? ""; var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.Recipient)));
        if (expectedHash.Length != 64 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expectedHash), Encoding.ASCII.GetBytes(actual))) return Invalid();
        var expected = config[$"PasswordRecovery:{input.Application}ResetUrl"] ?? $"https://{input.Application.ToLowerInvariant()}.zumaris.com/reset-password";
        if (!Uri.TryCreate(expected, UriKind.Absolute, out var page) || !Uri.TryCreate(input.ResetUrl, UriKind.Absolute, out var url) || !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Query) || url.GetLeftPart(UriPartial.Path) != page.AbsoluteUri || !(url.Scheme == "https" || url.Scheme == "http" && url.IsLoopback) || !url.Fragment.StartsWith("#token=", StringComparison.Ordinal)) return Invalid();
        var token = url.Fragment[7..]; if (token.Length != 43 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) return Invalid();
        await sender.Send(input, ct); return NoContent();
    }
}
