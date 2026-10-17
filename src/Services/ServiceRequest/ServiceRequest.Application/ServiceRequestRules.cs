using System.Text;

namespace Zumaris.ServiceRequests.Application;

public static class ServiceRequestRules
{
    public static CreateServiceRequest Normalize(CreateServiceRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        void Error(string field, string message) => errors[field] = [message];
        if (request.PropertyId == Guid.Empty) Error(nameof(request.PropertyId), "A nonempty PropertyId is required.");
        if (request.CategoryId == Guid.Empty) Error(nameof(request.CategoryId), "A nonempty CategoryId is required.");
        if (request.AreaTypeId == Guid.Empty) Error(nameof(request.AreaTypeId), "A nonempty AreaTypeId is required.");
        var description = request.Description?.Trim() ?? "";
        // Count Unicode code points, matching MySQL CHAR_LENGTH for non-BMP characters too.
        if (description.EnumerateRunes().Count() is < 20 or > 500)
            Error(nameof(request.Description), "Description must contain 20–500 characters after trimming.");
        if (!Enum.IsDefined(request.Urgency)) Error(nameof(request.Urgency), "Urgency must be Standard, Priority or Emergency.");

        var supplied = (request.PreferredDate.HasValue ? 1 : 0) + (request.PreferredStartTime.HasValue ? 1 : 0) +
            (request.PreferredEndTime.HasValue ? 1 : 0);
        if (supplied != 3 && (supplied != 0 || request.Urgency != Domain.RequestUrgency.Emergency))
            Error("PreferredDate", "Provide the date, start and end together. Only Emergency may omit all three.");
        if (request.PreferredStartTime.HasValue && request.PreferredEndTime.HasValue &&
            request.PreferredEndTime.Value <= request.PreferredStartTime.Value)
            Error("PreferredEndTime", "End time must follow start time on the same day.");
        // MySQL TIME is stored to whole seconds; never silently lose fractional precision.
        if ((request.PreferredStartTime?.Ticks ?? 0) % TimeSpan.TicksPerSecond != 0 ||
            (request.PreferredEndTime?.Ticks ?? 0) % TimeSpan.TicksPerSecond != 0)
            Error("PreferredStartTime", "Use whole-second time values.");
        if (errors.Count > 0) throw new RequestValidationException(errors);

        return request with { Description = description };
    }

    public static void ValidateFuture(CreateServiceRequest request, DateTimeOffset now, string timeZoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        if (request.PreferredDate.HasValue && request.PreferredStartTime.HasValue)
        {
            var preferred = request.PreferredDate.Value.ToDateTime(request.PreferredStartTime.Value);
            if (zone.IsInvalidTime(preferred) || zone.IsAmbiguousTime(preferred))
                throw new RequestValidationException(new() { ["PreferredStartTime"] = ["The local scheduling time is invalid or ambiguous."] });
            if (TimeZoneInfo.ConvertTimeToUtc(preferred, zone) <= now.UtcDateTime)
                throw new RequestValidationException(new() { ["PreferredDate"] = ["The preferred start must be in the future."] });
        }
    }
}
