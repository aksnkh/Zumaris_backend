using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Zumaris.ServiceRequests.Application.Quotes;
namespace Zumaris.ServiceRequests.Infrastructure;

public sealed class ProviderQuoteEligibility(HttpClient client):IProviderQuoteEligibility
{
    private async Task<ProviderQuoteIdentity> ReadAsync(string path,string bearer,CancellationToken ct)
    {
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,path);
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
            using var response=await client.SendAsync(request,ct);
            if(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)throw new QuoteForbiddenException();
            if(!response.IsSuccessStatusCode)throw new QuoteUnavailableException("Provider eligibility validation is temporarily unavailable.");
            var profile=await response.Content.ReadFromJsonAsync<ProviderQuoteIdentity>(cancellationToken:ct);
            if(profile is null||profile.ProviderId==Guid.Empty||!profile.IsVerified||string.IsNullOrWhiteSpace(profile.DisplayName))
                throw new QuoteUnavailableException("Provider eligibility validation is temporarily unavailable.");
            return profile;
        }
        catch(Exception ex)when(ex is HttpRequestException or JsonException or NotSupportedException || (ex is OperationCanceledException&&!ct.IsCancellationRequested))
        {throw new QuoteUnavailableException("Provider eligibility validation is temporarily unavailable.");}
    }
    public async Task<ProviderQuoteIdentity> GetAsync(Guid userId,int categoryId,string bearer,CancellationToken ct)
    {
        var profile=await ReadAsync($"api/providers/me/quote-profile?categoryId={categoryId}",bearer,ct);
        if(profile.UserId!=userId||profile.CategoryId!=categoryId)throw new QuoteForbiddenException();
        return profile;
    }
    public async Task VerifyAsync(Guid providerId,int categoryId,string bearer,CancellationToken ct)
    {
        var profile=await ReadAsync($"api/providers/{providerId}/quote-profile?categoryId={categoryId}",bearer,ct);
        if(profile.ProviderId!=providerId||profile.CategoryId!=categoryId)throw new QuoteForbiddenException();
    }
}
public sealed class QuoteDisplayLookup(HttpClient client):IQuoteDisplayLookup
{
    private sealed record Entry(int Id,string Name,string? IconUrl);
    public async Task<QuoteDisplay> GetAsync(int categoryId,int areaId,string bearer,CancellationToken ct)
    {
        string? category=null,area=null,icon=null;
        try
        {
            using var message=new HttpRequestMessage(HttpMethod.Get,$"api/categories/{categoryId}");
            message.Headers.Authorization=new("Bearer",bearer);
            using var response=await client.SendAsync(message,ct);
            if(response.IsSuccessStatusCode)
            {
                var entry=await response.Content.ReadFromJsonAsync<Entry>(cancellationToken:ct);
                if(entry?.Id==categoryId){category=entry.Name;icon=entry.IconUrl;}
            }
            using var areas=new HttpRequestMessage(HttpMethod.Get,"api/areas");areas.Headers.Authorization=new("Bearer",bearer);
            using var list=await client.SendAsync(areas,ct);
            if(list.IsSuccessStatusCode)area=(await list.Content.ReadFromJsonAsync<Entry[]>(cancellationToken:ct))?.FirstOrDefault(a=>a.Id==areaId)?.Name;
        }
        catch(Exception ex)when(ex is HttpRequestException or JsonException or NotSupportedException || (ex is OperationCanceledException&&!ct.IsCancellationRequested)) { }
        return new(category,area,icon);
    }
}
