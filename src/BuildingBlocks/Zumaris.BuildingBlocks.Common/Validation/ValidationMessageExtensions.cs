using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Zumaris.BuildingBlocks.Common.Validation;

public static class ValidationMessageExtensions
{
    public static IMvcBuilder AddValidationMessages(this IMvcBuilder builder)
    {
        builder.AddMvcOptions(options => options.Filters.Add<ValidationMessageFilter>());
        builder.ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                // Do not expose parser exceptions, input values or serializer implementation details.
                var errors = context.ModelState.Where(entry => entry.Value?.Errors.Count > 0)
                    .ToDictionary(entry => entry.Key, entry => new[] {
                        string.IsNullOrWhiteSpace(entry.Key) || entry.Key == "$"
                            ? "The request body is missing or invalid."
                            : $"Please provide a valid value for '{entry.Key.TrimStart('$', '.')}'."
                    });
                var details = new ValidationProblemDetails(errors) { Status = 400 };
                AddMessage(details, context.HttpContext.TraceIdentifier);
                return new BadRequestObjectResult(details);
            };
        });
        return builder;
    }

    internal static void AddMessage(ProblemDetails details, string traceId)
    {
        var messages = details is ValidationProblemDetails validation
            ? validation.Errors.Values.SelectMany(values => values).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToArray()
            : Array.Empty<string>();
        details.Extensions["message"] = messages.Length > 0 ? string.Join(" ", messages)
            : details.Detail ?? details.Title ?? "Please check the supplied information.";
        details.Extensions["statusCode"] = details.Status ?? 400;
        details.Extensions.TryAdd("traceId", traceId);
    }
}

// Includes automatic model validation, controller validation and handled domain exceptions.
public sealed class ValidationMessageFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: ProblemDetails details } result
            && (result.StatusCode ?? details.Status) == 400)
            ValidationMessageExtensions.AddMessage(details, context.HttpContext.TraceIdentifier);
    }
    public void OnResultExecuted(ResultExecutedContext context) { }
}
