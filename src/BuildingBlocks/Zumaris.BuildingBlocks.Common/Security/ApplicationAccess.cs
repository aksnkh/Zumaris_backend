using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Zumaris.BuildingBlocks.Common.Responses;

namespace Zumaris.BuildingBlocks.Common.Security;

public static class Applications
{
    public const string App = "App", Tech = "Tech", Admin = "Admin";
    public static bool IsValid(string? application) => application is App or Tech or Admin;
    public static string Role(string application) => application switch
    { App => "Customer", Tech => "Provider", Admin => "Admin", _ => "" };
    public static bool HasValidContext(ClaimsPrincipal user)
    {
        var apps = user.FindAll("application").ToArray();
        if (apps.Length != 1 || !IsValid(apps[0].Value)) return false;
        var roles = user.FindAll("role").ToArray();
        if (roles.Length != 1 || roles[0].Value != Role(apps[0].Value)) return false;
        return apps[0].Value != Tech || (user.FindAll("providerId").Count() == 1 &&
            Guid.TryParse(user.FindFirst("providerId")?.Value, out var id) && id != Guid.Empty);
    }
}

// Method metadata overrides the controller/default scope, for genuinely shared actions only.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ApplicationAccessAttribute(params string[] applications) : Attribute
{
    public IReadOnlyList<string> Applications { get; } = applications;
}

// Only dedicated server-to-server controllers using a separate authentication scheme may use this.
[AttributeUsage(AttributeTargets.Class)]
public sealed class InternalApplicationAccessAttribute : Attribute;

public sealed class ApplicationAccessFilter(string[] defaults) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action) return;
        if (action.MethodInfo.IsDefined(typeof(AllowAnonymousAttribute)) ||
            action.ControllerTypeInfo.IsDefined(typeof(AllowAnonymousAttribute))) return;
        if (action.ControllerTypeInfo.IsDefined(typeof(InternalApplicationAccessAttribute))) return;
        // Authorization middleware owns anonymous 401 responses. Public icons stay public.
        if (context.HttpContext.User.Identity?.IsAuthenticated != true) return;
        var allowed = action.MethodInfo.GetCustomAttribute<ApplicationAccessAttribute>()?.Applications
            ?? action.ControllerTypeInfo.GetCustomAttribute<ApplicationAccessAttribute>()?.Applications ?? defaults;
        var user = context.HttpContext.User;
        if (Applications.HasValidContext(user) && allowed.Contains(user.FindFirst("application")!.Value)) return;
        context.Result = new JsonResult(ApiResponseError.Create(403,
            "This token is not authorized for this application endpoint.", context.HttpContext.TraceIdentifier),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter<MessageLevel>() } }) { StatusCode = 403 };
    }
}

public static class ApplicationAccessExtensions
{
    public static IServiceCollection AddApplicationIsolation(this IServiceCollection services, params string[] defaults)
    {
        if (defaults.Length == 0 || defaults.Any(x => !Applications.IsValid(x)))
            throw new ArgumentException("Valid application defaults are required.");
        services.Configure<MvcOptions>(options => options.Filters.Add(new ApplicationAccessFilter(defaults)));
        return services;
    }
}
