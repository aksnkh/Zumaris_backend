using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Zumaris.ServiceRequests.Application;
namespace Zumaris.ServiceRequests.Infrastructure;

public sealed class CategoryIconLookup(HttpClient client, ILogger<CategoryIconLookup> logger) : ICategoryIconLookup
{
    private sealed record Category(int Id, string? IconUrl);
    public async Task<string?> GetIconUrlAsync(int categoryId, CancellationToken ct)
    {
        if (categoryId <= 0) return null;
        try
        {
            using var response = await client.GetAsync($"api/categories/{categoryId}", ct);
            if (!response.IsSuccessStatusCode) return null;
            var category = await response.Content.ReadFromJsonAsync<Category>(cancellationToken: ct);
            return category?.Id == categoryId && !string.IsNullOrWhiteSpace(category.IconUrl) ? category.IconUrl : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException ||
            (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("Category icon lookup unavailable for category {CategoryId}.",categoryId);
            return null;
        }
    }
}
