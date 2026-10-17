using Zumaris.ServiceRequests.Domain;

namespace Zumaris.ServiceRequests.Application;

public sealed record UpdateServiceRequest(Guid PropertyId, Guid CategoryId, Guid AreaTypeId, string Description,
    RequestUrgency Urgency, DateOnly? PreferredDate, TimeOnly? PreferredStartTime, TimeOnly? PreferredEndTime, int Version)
{
    public CreateServiceRequest AsCreate() => new(PropertyId, CategoryId, AreaTypeId, Description, Urgency,
        PreferredDate, PreferredStartTime, PreferredEndTime);
}
public sealed record CancelServiceRequest(string Reason, int Version);
public sealed record PhotoRecord(Guid Id, Guid RequestId, string FilePath, int Slot, DateTime CreatedAtUtc);
public sealed record RequestPhotoDto(Guid Id, string DownloadPath);
public sealed record ServiceRequestDto(ServiceRequestRecord Request, IReadOnlyList<RequestPhotoDto> Photos);
public sealed record RequestPage(IReadOnlyList<ServiceRequestDto> Items, long TotalCount, int Page, int PageSize);
public sealed record RequestStatusHistoryDto(Guid Id, RequestStatus? FromStatus, RequestStatus ToStatus,
    Guid ActorUserId, DateTime OccurredAtUtc, string? Reason);
public sealed record PhotoUpload(Stream Content, long Length);
public sealed record StoredPhoto(Guid Id, string FilePath);
public sealed record PhotoUploadResult(IReadOnlyList<RequestPhotoDto> Photos, int Version);
public sealed record PhotoDownload(Stream Content, string ContentType, string FileName);

public interface IRequestManagementService
{
    Task<ServiceRequestDto> GetAsync(Guid userId, Guid requestId, CancellationToken ct);
    Task<RequestPage> ListAsync(Guid userId, RequestStatus? status, int page, int pageSize, CancellationToken ct);
    Task<ServiceRequestDto> UpdateAsync(Guid userId, Guid requestId, UpdateServiceRequest request, CancellationToken ct);
    Task<ServiceRequestDto> CancelAsync(Guid userId, Guid requestId, CancelServiceRequest request, CancellationToken ct);
    Task<IReadOnlyList<RequestStatusHistoryDto>> TimelineAsync(Guid userId, Guid requestId, CancellationToken ct);
    Task<PhotoUploadResult> AddPhotosAsync(Guid userId, Guid requestId, int version, IReadOnlyList<PhotoUpload> photos, CancellationToken ct);
    Task<PhotoDownload> DownloadAsync(Guid userId, Guid requestId, Guid photoId, CancellationToken ct);
}

public interface IRequestLifecycleRepository
{
    Task<ServiceRequestRecord?> GetAsync(Guid userId, Guid requestId, CancellationToken ct);
    Task<(IReadOnlyList<ServiceRequestRecord> Items, long Total)> ListAsync(Guid userId, RequestStatus? status, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<PhotoRecord>> PhotosAsync(Guid userId, IReadOnlyList<Guid> requestIds, CancellationToken ct);
    Task<ServiceRequestRecord> UpdateAsync(Guid userId, Guid requestId, CreateServiceRequest request, int version, DateTime now, CancellationToken ct);
    Task<ServiceRequestRecord> CancelAsync(Guid userId, Guid requestId, string reason, int version, DateTime now, CancellationToken ct);
    Task<IReadOnlyList<RequestStatusHistoryDto>> TimelineAsync(Guid userId, Guid requestId, CancellationToken ct);
    Task<int> AttachPhotosAsync(Guid userId, Guid requestId, int version, IReadOnlyList<StoredPhoto> photos, DateTime now, CancellationToken ct);
}

public interface IPhotoStorage
{
    Task<StoredPhoto> SaveAsync(Guid requestId, PhotoUpload photo, CancellationToken ct);
    Task DeleteAsync(string relativePath);
    Task<PhotoDownload> OpenAsync(string relativePath, CancellationToken ct);
}

public sealed class RequestNotFoundException() : Exception("Request or photo was not found.");
public sealed class RequestConflictException(string message) : Exception(message);
