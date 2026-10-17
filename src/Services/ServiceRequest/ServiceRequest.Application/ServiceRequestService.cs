using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zumaris.ServiceRequests.Domain;

namespace Zumaris.ServiceRequests.Application;

public sealed class ServiceRequestService(
    IServiceRequestRepository repository, TimeProvider clock, SchedulingSettings settings) : IServiceRequestService
{
    public async Task<CreateResult> CreateAsync(Guid userId, CreateServiceRequest request, string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty) throw new UnauthorizedAccessException();
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
        if (idempotencyKey is not null && (idempotencyKey.Length is < 8 or > 128 ||
            idempotencyKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.')))
            Error("Idempotency-Key", "Use 8–128 ASCII letters, digits, hyphens, underscores or periods.");

        var supplied = (request.PreferredDate.HasValue ? 1 : 0) + (request.PreferredStartTime.HasValue ? 1 : 0) +
            (request.PreferredEndTime.HasValue ? 1 : 0);
        if (supplied != 3 && (supplied != 0 || request.Urgency != RequestUrgency.Emergency))
            Error("PreferredDate", "Provide the date, start and end together. Only Emergency may omit all three.");
        if (request.PreferredStartTime.HasValue && request.PreferredEndTime.HasValue &&
            request.PreferredEndTime.Value <= request.PreferredStartTime.Value)
            Error("PreferredEndTime", "End time must follow start time on the same day.");
        // MySQL TIME is stored to whole seconds; never silently lose fractional precision.
        if ((request.PreferredStartTime?.Ticks ?? 0) % TimeSpan.TicksPerSecond != 0 ||
            (request.PreferredEndTime?.Ticks ?? 0) % TimeSpan.TicksPerSecond != 0)
            Error("PreferredStartTime", "Use whole-second time values.");
        if (errors.Count > 0) throw new RequestValidationException(errors);

        var normalized = request with { Description = description };
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized)))).ToLowerInvariant();
        // A previously successful retry must still work after its scheduling time has passed.
        if (idempotencyKey is not null)
        {
            var existing = await repository.FindByKeyAsync(userId, idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                if (existing.RequestHash != hash) throw new IdempotencyConflictException();
                return new CreateResult(ToCreated(existing.Request), true);
            }
        }

        var now = clock.GetUtcNow();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        if (request.PreferredDate.HasValue && request.PreferredStartTime.HasValue)
        {
            var preferred = request.PreferredDate.Value.ToDateTime(request.PreferredStartTime.Value);
            if (zone.IsInvalidTime(preferred) || zone.IsAmbiguousTime(preferred))
                throw new RequestValidationException(new() { ["PreferredStartTime"] = ["The local scheduling time is invalid or ambiguous."] });
            if (TimeZoneInfo.ConvertTimeToUtc(preferred, zone) <= now.UtcDateTime)
                throw new RequestValidationException(new() { ["PreferredDate"] = ["The preferred start must be in the future."] });
        }
        var record = new ServiceRequestRecord(Guid.NewGuid(), userId, request.PropertyId, request.CategoryId,
            request.AreaTypeId, description, request.Urgency, request.PreferredDate, request.PreferredStartTime,
            request.PreferredEndTime, settings.TimeZoneId, RequestStatus.Submitted, null, now.UtcDateTime, null, 1);
        return await repository.CreateAsync(record, idempotencyKey, hash, cancellationToken);
    }

    public static CreatedServiceRequest ToCreated(ServiceRequestRecord request) =>
        new(request.RequestId, request.Status, request.CreatedAtUtc, request.Version);
}
