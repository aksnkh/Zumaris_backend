namespace Zumaris.ServiceRequests.Application;

public sealed class ServiceHistoryService(IServiceHistoryRepository repository) : IServiceHistoryService
{
    public Task<PagedResult<ServiceHistoryDto>> GetHistoryAsync(Guid userId, HistoryFilterRequest request, CancellationToken ct)
    {
        if (userId == Guid.Empty) throw new UnauthorizedAccessException();
        var status = request.Status?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => null,
            "completed" => "Completed", "cancelled" => "Cancelled", "paid" => "Paid",
            _ => throw new RequestValidationException(new() { ["status"] = ["Use All, Completed, Cancelled or Paid."] })
        };
        if (request.PageNumber < 1 || request.PageSize is < 1 or > 100)
            throw new RequestValidationException(new() { ["pagination"] = ["pageNumber must be positive; pageSize must be between 1 and 100."] });
        return repository.GetHistoryAsync(userId, request with { Status = status }, ct);
    }
    public Task<ServiceHistoryDetailDto?> GetByIdAsync(Guid userId, Guid historyId, CancellationToken ct)
    {
        if (userId == Guid.Empty) throw new UnauthorizedAccessException();
        return repository.GetByIdAsync(userId, historyId, ct);
    }
}
