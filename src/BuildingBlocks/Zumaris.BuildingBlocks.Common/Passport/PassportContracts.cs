using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Zumaris.BuildingBlocks.Common.Responses;
namespace Zumaris.BuildingBlocks.Common.Passport;
public sealed class PassportException(int status, string message) : Exception(message) { public int Status { get; } = status; }
public sealed record PassportPage<T>(IReadOnlyList<T> Items, long TotalCount, int PageNumber, int PageSize);
public sealed record PropertyPermission(Guid PropertyId, bool CanWrite);
public sealed record PassportEvent(string EventId, Guid PropertyId, DateTime OccurredAtUtc, string Source, string Type, string Title, string? Description, Guid RelatedId, decimal? Cost = null, string? CurrencyCode = null);
public sealed record EventQuery(int PageNumber = 1, int PageSize = 20, DateTimeOffset? From = null, DateTimeOffset? To = null, string? Type = null)
{
    public void Validate() { if (PageNumber < 1 || PageNumber > 100 || PageSize < 1 || PageSize > 100 || Type?.Length > 64 || From > To) throw new PassportException(400, "Use pageNumber 1–100, pageSize 1–100 and a valid date range."); }
}
public static class PassportResponse
{
    public static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public static Response<T> Of<T>(T payload, string message = "Retrieved successfully.", int status = 200) => new(payload) { StatusCode = status, Message = message, MessageLevel = MessageLevel.Cif, Title = status < 400 ? "Success" : "Request failed" };
    public static void Validate(object input)
    {
        var errors = new List<ValidationResult>(); Validator.TryValidateObject(input, new ValidationContext(input), errors, true);
        foreach (var parameter in input.GetType().GetConstructors().OrderByDescending(c => c.GetParameters().Length).First().GetParameters())
        {
            var attributes = parameter.GetCustomAttributes(typeof(ValidationAttribute), true).Cast<ValidationAttribute>();
            var property = input.GetType().GetProperty(parameter.Name!); if (property is not null) Validator.TryValidateValue(property.GetValue(input), new ValidationContext(input) { MemberName = property.Name }, errors, attributes);
        }
        if (errors.Count > 0) throw new PassportException(400, string.Join(" ", errors.Select(x => x.ErrorMessage)));
    }
}
public sealed class PassportValidationResponse : Response<object?>
{
    public required IDictionary<string, string[]> Errors { get; init; }
}
public sealed class PassportExceptionFilter(ILogger<PassportExceptionFilter> logger) : IExceptionFilter, IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext c)
    {
        if (c.Result is ObjectResult { Value: ValidationProblemDetails details } r && (r.StatusCode ?? details.Status) == 400)
            c.Result = new BadRequestObjectResult(new PassportValidationResponse { StatusCode = 400, Title = "Validation failed", Message = "Please check the supplied information.", MessageLevel = MessageLevel.Cif, Errors = details.Errors, Payload = null });
    }
    public void OnResultExecuted(ResultExecutedContext c) { }
    public void OnException(ExceptionContext c) { var ex = c.Exception as PassportException; int status = ex?.Status ?? 503; if (ex is null) logger.LogError(c.Exception, "Passport operation failed."); c.Result = new ObjectResult(PassportResponse.Of<object?>(null, ex?.Message ?? "The service is temporarily unavailable.", status)) { StatusCode = status }; c.ExceptionHandled = true; }
}
