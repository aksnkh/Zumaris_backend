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
        var normalized = ServiceRequestRules.Normalize(request);
        if (idempotencyKey is not null && (idempotencyKey.Length is < 8 or > 128 ||
            idempotencyKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.')))
            throw new RequestValidationException(new() { ["Idempotency-Key"] = ["Use 8–128 ASCII letters, digits, hyphens, underscores or periods."] });
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
        ServiceRequestRules.ValidateFuture(normalized, now, settings.TimeZoneId);
        var record = new ServiceRequestRecord(Guid.NewGuid(), userId, request.PropertyId, request.CategoryId,
            request.AreaTypeId, normalized.Description, request.Urgency, request.PreferredDate, request.PreferredStartTime,
            request.PreferredEndTime, settings.TimeZoneId, RequestStatus.Submitted, null, now.UtcDateTime, null, 1);
        return await repository.CreateAsync(record, idempotencyKey, hash, cancellationToken);
    }

    public static CreatedServiceRequest ToCreated(ServiceRequestRecord request) =>
        new(request.RequestId, request.Status, request.CreatedAtUtc, request.Version);
}
