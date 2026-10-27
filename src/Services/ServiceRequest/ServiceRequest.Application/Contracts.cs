using Zumaris.ServiceRequests.Domain;

namespace Zumaris.ServiceRequests.Application;

public sealed record CreateServiceRequest(
    Guid PropertyId, int CategoryId, int AreaTypeId, string Description,
    RequestUrgency Urgency, DateOnly? PreferredDate, TimeOnly? PreferredStartTime, TimeOnly? PreferredEndTime);

public sealed record CreatedServiceRequest(Guid RequestId, RequestStatus Status, DateTime CreatedAtUtc, int Version);
public sealed record CreateResult(CreatedServiceRequest Request, bool IsReplay);
public sealed record PersistedRequest(ServiceRequestRecord Request, string RequestHash);

public interface IServiceRequestService
{
    Task<CreateResult> CreateAsync(Guid userId, CreateServiceRequest request, string? idempotencyKey,
        CancellationToken cancellationToken = default);
}

public interface IServiceRequestRepository
{
    Task<PersistedRequest?> FindByKeyAsync(Guid userId, string key, CancellationToken cancellationToken);
    Task<CreateResult> CreateAsync(ServiceRequestRecord request, string? key, string requestHash,
        CancellationToken cancellationToken);
}

public interface IIdentitySessionValidator
{
    Task<bool> IsActiveAsync(string accessToken, bool originalRequestIsHttps, CancellationToken cancellationToken);
}

public sealed class RequestValidationException(Dictionary<string, string[]> errors) : Exception("Request validation failed.")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}
public sealed class IdempotencyConflictException() : Exception("Idempotency-Key was already used with a different request.");

public sealed class SchedulingSettings
{
    public string TimeZoneId { get; set; } = "Asia/Tehran";
}
