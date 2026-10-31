namespace Zumaris.ServiceRequests.Application;

public sealed record HistoryFilterRequest(string? Status = null, int PageNumber = 1, int PageSize = 20);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, long TotalCount)
{
    public long TotalPages => TotalCount / PageSize + (TotalCount % PageSize == 0 ? 0 : 1);
}
public sealed record ServiceHistoryDto(Guid RequestId, string RequestNumber, string? ServiceName,
    string? CategoryName, string? CategoryIcon, string? ServiceArea, string? TechnicianName,
    DateTime? ServiceDate, string Status, string PaymentStatus, decimal? TotalAmount,
    string? CurrencyCode, DateTime CreatedAt)
{
    public int CategoryId { get; init; }
    public string? CategoryIconUrl { get; init; }
}
public sealed record ServiceHistoryDetailDto(Guid HistoryId, Guid RequestId, string RequestNumber,
    Guid UserId, Guid PropertyId, string? PropertyAddress, int CategoryId, string? CategoryName,
    string? ServiceName, string? ServiceArea, string? Description, Guid? TechnicianId,
    string? TechnicianName, DateTime? ServiceDate, DateTime? CompletedAt, DateTime? CancelledAt,
    string? CancellationReason, string Status, string PaymentStatus, decimal? TotalAmount,
    string? CurrencyCode, DateTime CreatedAt, DateTime? UpdatedAt)
{
    public string? CategoryIconUrl { get; init; }
}

public interface ICategoryIconLookup
{
    Task<string?> GetIconUrlAsync(int categoryId, CancellationToken ct);
}

public interface IServiceHistoryRepository
{
    Task<PagedResult<ServiceHistoryDto>> GetHistoryAsync(Guid userId, HistoryFilterRequest request, CancellationToken ct);
    Task<ServiceHistoryDetailDto?> GetByIdAsync(Guid userId, Guid historyId, CancellationToken ct);
}
public interface IServiceHistoryService
{
    Task<PagedResult<ServiceHistoryDto>> GetHistoryAsync(Guid userId, HistoryFilterRequest request, CancellationToken ct);
    Task<ServiceHistoryDetailDto?> GetByIdAsync(Guid userId, Guid historyId, CancellationToken ct);
}
