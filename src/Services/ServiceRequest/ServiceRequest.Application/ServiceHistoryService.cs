namespace Zumaris.ServiceRequests.Application;

public sealed class ServiceHistoryService(IServiceHistoryRepository repository, ICategoryIconLookup icons) : IServiceHistoryService
{
    public async Task<PagedResult<ServiceHistoryDto>> GetHistoryAsync(Guid userId, HistoryFilterRequest request, CancellationToken ct)
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
        var page = await repository.GetHistoryAsync(userId, request with { Status = status }, ct);
        // At most one lookup per distinct category on the page; bounded concurrency.
        var urls = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
        using var lookupBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lookupBudget.CancelAfter(TimeSpan.FromSeconds(3));
        await Parallel.ForEachAsync(page.Items.Select(item => item.CategoryId).Where(id => id > 0).Distinct(),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (id, token) =>
            {
                try
                {
                    var url = await icons.GetIconUrlAsync(id, lookupBudget.Token);
                    if (!string.IsNullOrWhiteSpace(url)) urls[id] = url;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            });
        return page with { Items = page.Items.Select(item => {
            var url = urls.TryGetValue(item.CategoryId, out var current) ? current : item.CategoryIcon;
            return item with { CategoryIcon = url, CategoryIconUrl = url };
        }).ToArray() };
    }
    public async Task<ServiceHistoryDetailDto?> GetByIdAsync(Guid userId, Guid historyId, CancellationToken ct)
    {
        if (userId == Guid.Empty) throw new UnauthorizedAccessException();
        var item = await repository.GetByIdAsync(userId, historyId, ct);
        if (item is null) return null;
        return item with { CategoryIconUrl = await icons.GetIconUrlAsync(item.CategoryId, ct) };
    }
}
