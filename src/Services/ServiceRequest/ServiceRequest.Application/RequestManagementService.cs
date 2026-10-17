using System.Text;
using Zumaris.ServiceRequests.Domain;

namespace Zumaris.ServiceRequests.Application;

public sealed class RequestManagementService(IRequestLifecycleRepository repository, IPhotoStorage storage,
    TimeProvider clock) : IRequestManagementService
{
    private static RequestPhotoDto PhotoDto(PhotoRecord p) => new(p.Id, $"api/service-requests/{p.RequestId}/photos/{p.Id}");
    private static void Version(int version)
    {
        if (version < 1) throw new RequestValidationException(new() { ["Version"] = ["Version must be positive."] });
    }
    private async Task<ServiceRequestRecord> Owned(Guid userId, Guid requestId, CancellationToken ct) =>
        await repository.GetAsync(userId, requestId, ct) ?? throw new RequestNotFoundException();

    public async Task<ServiceRequestDto> GetAsync(Guid userId, Guid requestId, CancellationToken ct)
    {
        var request = await Owned(userId, requestId, ct);
        var photos = await repository.PhotosAsync(userId, [requestId], ct);
        return new(request, photos.Select(PhotoDto).ToList());
    }

    public async Task<RequestPage> ListAsync(Guid userId, RequestStatus? status, int page, int pageSize, CancellationToken ct)
    {
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100 || (status.HasValue && !Enum.IsDefined(status.Value)))
            throw new RequestValidationException(new() { ["Pagination"] = ["Use page 1–1000000, pageSize 1–100 and a known status."] });
        var (items, total) = await repository.ListAsync(userId, status, page, pageSize, ct);
        var photos = await repository.PhotosAsync(userId, items.Select(r => r.RequestId).ToList(), ct);
        return new(items.Select(r => new ServiceRequestDto(r, photos.Where(p => p.RequestId == r.RequestId).Select(PhotoDto).ToList())).ToList(), total, page, pageSize);
    }

    public async Task<ServiceRequestDto> UpdateAsync(Guid userId, Guid requestId, UpdateServiceRequest request, CancellationToken ct)
    {
        Version(request.Version);
        var existing = await Owned(userId, requestId, ct);
        if (!RequestStateRules.CanEdit(existing.Status)) throw new RequestConflictException("Only Submitted requests can be edited.");
        var normalized = ServiceRequestRules.Normalize(request.AsCreate());
        ServiceRequestRules.ValidateFuture(normalized, clock.GetUtcNow(), existing.TimeZoneId);
        var updated = await repository.UpdateAsync(userId, requestId, normalized, request.Version, clock.GetUtcNow().UtcDateTime, ct);
        var photos = await repository.PhotosAsync(userId, [requestId], ct);
        return new(updated, photos.Select(PhotoDto).ToList());
    }

    public async Task<ServiceRequestDto> CancelAsync(Guid userId, Guid requestId, CancelServiceRequest request, CancellationToken ct)
    {
        Version(request.Version);
        var reason = request.Reason?.Trim() ?? "";
        if (reason.EnumerateRunes().Count() is < 3 or > 500)
            throw new RequestValidationException(new() { ["Reason"] = ["Cancellation reason must contain 3–500 characters."] });
        var existing = await Owned(userId, requestId, ct);
        if (!RequestStateRules.CanCancel(existing.Status)) throw new RequestConflictException("This request can no longer be cancelled.");
        var updated = await repository.CancelAsync(userId, requestId, reason, request.Version, clock.GetUtcNow().UtcDateTime, ct);
        var photos = await repository.PhotosAsync(userId, [requestId], ct);
        return new(updated, photos.Select(PhotoDto).ToList());
    }

    public async Task<IReadOnlyList<RequestStatusHistoryDto>> TimelineAsync(Guid userId, Guid requestId, CancellationToken ct)
    {
        await Owned(userId, requestId, ct);
        return await repository.TimelineAsync(userId, requestId, ct);
    }

    public async Task<PhotoUploadResult> AddPhotosAsync(Guid userId, Guid requestId, int version, IReadOnlyList<PhotoUpload> photos, CancellationToken ct)
    {
        Version(version);
        var request = await Owned(userId, requestId, ct);
        if (request.Version != version) throw new RequestConflictException("Request version has changed. Reload before retrying.");
        if (!RequestStateRules.CanAddPhotos(request.Status))
            throw new RequestConflictException("Photos can only be added to Submitted or Assigned requests.");
        if (photos.Count is < 1 or > 8)
            throw new RequestValidationException(new() { ["Photos"] = ["Send 1–8 photos."] });
        if ((await repository.PhotosAsync(userId, [requestId], ct)).Count + photos.Count > 8)
            throw new RequestConflictException("A request can contain at most 8 photos.");
        var saved = new List<StoredPhoto>();
        // Until DB attachment begins, any failure is safe to compensate on disk.
        try
        {
            foreach (var photo in photos) saved.Add(await storage.SaveAsync(requestId, photo, ct));
        }
        catch
        {
            foreach (var photo in saved) await storage.DeleteAsync(photo.FilePath);
            throw;
        }
        int updatedVersion;
        try { updatedVersion = await repository.AttachPhotosAsync(userId, requestId, version, saved, clock.GetUtcNow().UtcDateTime, ct); }
        catch (Exception ex) when (ex is RequestNotFoundException or RequestConflictException)
        {
            // These errors are guaranteed to occur before DB commit.
            foreach (var photo in saved) await storage.DeleteAsync(photo.FilePath);
            throw;
        }
        // On an unknown DB/commit error retain files: deleting them could break a committed row.
        // They can be reconciled with DB metadata after an outage; see deployment notes.
        return new(saved.Select(p => new RequestPhotoDto(p.Id, $"api/service-requests/{requestId}/photos/{p.Id}")).ToList(), updatedVersion);
    }

    public async Task<PhotoDownload> DownloadAsync(Guid userId, Guid requestId, Guid photoId, CancellationToken ct)
    {
        await Owned(userId, requestId, ct);
        var photo = (await repository.PhotosAsync(userId, [requestId], ct)).SingleOrDefault(p => p.Id == photoId)
            ?? throw new RequestNotFoundException();
        return await storage.OpenAsync(photo.FilePath, ct);
    }
}
