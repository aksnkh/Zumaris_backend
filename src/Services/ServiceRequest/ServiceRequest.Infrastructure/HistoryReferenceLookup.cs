using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Zumaris.ServiceRequests.Application;
namespace Zumaris.ServiceRequests.Infrastructure;

public sealed class HistoryReferenceLookup(HttpClient client,IConfiguration configuration,ILogger<HistoryReferenceLookup> logger):IHistoryReferenceLookup
{
    private readonly Uri catalog=Root(configuration["ServiceCatalog:BaseUrl"]??"http://127.0.0.1:5006/");
    private readonly Uri properties=Root(configuration["Property:BaseUrl"]??"http://127.0.0.1:5003/");
    private readonly Uri providers=Root(configuration["Provider:BaseUrl"]??"http://127.0.0.1:5007/");
    private static Uri Root(string value)
    {
        var uri=new Uri(value);
        if(!uri.IsAbsoluteUri||!string.IsNullOrEmpty(uri.UserInfo)||!(uri.Scheme=="https"||(uri.Scheme=="http"&&uri.IsLoopback))||!uri.AbsoluteUri.EndsWith('/'))
            throw new InvalidOperationException("History reference BaseUrl must use HTTPS or loopback HTTP and end with a slash.");
        return uri;
    }
    private async Task<T?> ReadAsync<T>(Uri root,string path,string? bearer,CancellationToken ct)
    {
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,new Uri(root,path));
            if(!string.IsNullOrWhiteSpace(bearer))request.Headers.Authorization=new("Bearer",bearer);
            if(root.IsLoopback)request.Headers.Add("X-Forwarded-Proto","https");
            using var response=await client.SendAsync(request,ct);
            if(!response.IsSuccessStatusCode){logger.LogWarning("History reference {Resource} returned {Status}.",path,(int)response.StatusCode);return default;}
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken:ct);
        }
        catch(Exception ex)when(ex is HttpRequestException or JsonException or NotSupportedException || (ex is OperationCanceledException&&!ct.IsCancellationRequested))
        {logger.LogWarning("History reference {Resource} is unavailable.",path);return default;}
    }
    public async Task<HistoryCategoryReference?> CategoryAsync(int id,CancellationToken ct)
    {
        if(id<=0)return null;
        var item=await ReadAsync<HistoryCategoryReference>(catalog,$"api/categories/{id}",null,ct);
        return item?.Id==id?item:null;
    }
    private sealed record Area(int Id,string Name);
    public async Task<IReadOnlyDictionary<int,string>> AreasAsync(string? bearer,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(bearer))return new Dictionary<int,string>();
        var items=await ReadAsync<Area[]>(catalog,"api/areas",bearer,ct);
        return (items??[]).Where(a=>a.Id>0&&!string.IsNullOrWhiteSpace(a.Name)).GroupBy(a=>a.Id).ToDictionary(g=>g.Key,g=>g.First().Name);
    }
    private sealed record Property(Guid PropertyId,string? AddressLine,string? City,string? Postcode,string? CountryCode);
    public async Task<string?> PropertyAddressAsync(Guid id,string? bearer,CancellationToken ct)
    {
        if(id==Guid.Empty||string.IsNullOrWhiteSpace(bearer))return null;
        var item=await ReadAsync<Property>(properties,$"api/properties/{id}",bearer,ct);
        if(item?.PropertyId!=id)return null;
        var address=string.Join(", ",new[]{item.AddressLine,item.City,item.Postcode,item.CountryCode}.Where(v=>!string.IsNullOrWhiteSpace(v)));
        return address.Length==0?null:address;
    }
    private sealed record Provider(Guid ProviderId,string? DisplayName);
    public async Task<string?> ProviderNameAsync(Guid id,string? bearer,CancellationToken ct)
    {
        if(id==Guid.Empty||string.IsNullOrWhiteSpace(bearer))return null;
        var item=await ReadAsync<Provider>(providers,$"api/providers/{id}/summary",bearer,ct);
        return item?.ProviderId==id?item.DisplayName:null;
    }
}
