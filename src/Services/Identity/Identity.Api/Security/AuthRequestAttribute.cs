using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Identity.Api.Security;

// A non-simple header plus explicit CORS origins prevents cross-origin form/CSRF requests.
[AttributeUsage(AttributeTargets.Class)]
public sealed class AuthRequestAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsGet(request.Method) && request.Headers["X-Zumaris-CSRF"] != "1")
            context.Result = new BadRequestObjectResult(new ProblemDetails
            {
                Status = 400, Title = "The X-Zumaris-CSRF: 1 header is required."
            });
    }
}
