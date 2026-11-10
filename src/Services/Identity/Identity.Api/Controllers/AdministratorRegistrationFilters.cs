using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Identity.Api.Security;
using Identity.Application.Abstractions.Persistence;
using Identity.Application.Features.Administration;
using Identity.Application.Features.Signup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.BuildingBlocks.Common.Security;

namespace Identity.Api.Controllers;

public sealed class ActiveAdministratorFilter(IPlatformAdminRepository administrators, ILogger<ActiveAdministratorFilter> logger) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
        var user = context.HttpContext.User;
        if (!Applications.HasValidContext(user) || user.FindFirst("application")?.Value != Applications.Admin ||
            !Guid.TryParse(user.FindFirst("sub")?.Value, out var actor))
        { Deny(context, 403, "Active administrator access is required."); return; }
        try
        {
            if (!await administrators.IsAdminAsync(actor, context.HttpContext.RequestAborted))
                Deny(context, 403, "Active administrator access is required.");
        }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Administrator membership validation failed.");
            Deny(context, 503, "Administrator access validation is temporarily unavailable.");
        }
    }
    internal static void Deny(AuthorizationFilterContext context, int status, string message) =>
        context.Result = new ObjectResult(ApiResponseError.Create(status, message, context.HttpContext.TraceIdentifier)) { StatusCode = status };
}

public sealed class AdministratorBootstrapKeyFilter(IConfiguration configuration) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
        if (!string.Equals(configuration["AdminBootstrap:Enabled"], "true", StringComparison.OrdinalIgnoreCase))
        { ActiveAdministratorFilter.Deny(context, 404, "Administrator bootstrap is disabled."); return; }
        var expected = configuration["AdminBootstrap:Key"] ?? "";
        if (Encoding.UTF8.GetByteCount(expected) is <32 or >512)
        { ActiveAdministratorFilter.Deny(context, 503, "Administrator bootstrap is not configured."); return; }
        var headers = context.HttpContext.Request.Headers["X-Zumaris-Bootstrap-Key"];
        var supplied = headers.FirstOrDefault() ?? "";
        if (headers.Count != 1 || supplied.Length > 512 || !CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            ActiveAdministratorFilter.Deny(context, 401, "A valid administrator bootstrap key is required.");
    }
}
