using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Zumaris.ServiceRequests.Application;
using Microsoft.Extensions.Configuration;
namespace Zumaris.ServiceRequests.Infrastructure;

public sealed class JobContactLookup(IHttpClientFactory factory, IConfiguration config) : IJobContactLookup
{
    // Transport projections only; no dependency on another service's domain/project/schema.
    public sealed record Envelope<T>(T Payload);
    public sealed record CustomerProfile(Guid UserId, string? FirstName, string? LastName, string? Phone);
    public sealed record PropertyLocation(Guid PropertyId, string? AddressLine, string? City, string? Postcode, decimal? Latitude, decimal? Longitude);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<T?> Read<T>(string service, string path, string bearer, CancellationToken ct) where T : class
    {
        var configured = config[service + ":BaseUrl"] ?? (service == "Identity" ? "http://127.0.0.1:5001/" : "http://127.0.0.1:5003/");
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !uri.AbsoluteUri.EndsWith('/') || !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback))
            throw new JobContactUnavailableException();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(uri, path));
        request.Headers.Authorization = new("Bearer", bearer);
        if (uri.IsLoopback) request.Headers.Add("X-Forwarded-Proto", "https");
        try
        {
            using var response = await factory.CreateClient("job-contact").SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode) throw new JobContactUnavailableException();
            return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new JobContactUnavailableException();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        { throw new JobContactUnavailableException(); }
    }

    public async Task<JobContactSnapshot> Get(Guid customerId, Guid propertyId, string bearer, CancellationToken ct)
    {
        var customer = Read<Envelope<CustomerProfile>>("Identity", "api/profile/me", bearer, ct);
        var property = Read<PropertyLocation>("Property", $"api/properties/{propertyId}", bearer, ct);
        await Task.WhenAll(customer, property);
        var c = customer.Result?.Payload;
        var p = property.Result;
        if (customer.Result is not null && c is null || c is not null && (c.UserId != customerId || c.FirstName?.EnumerateRunes().Count() > 100 || c.LastName?.EnumerateRunes().Count() > 100 || c.Phone?.Length > 40) ||
            p is not null && (p.PropertyId != propertyId || p.Latitude.HasValue != p.Longitude.HasValue || p.Latitude is < -90 or > 90 || p.Longitude is < -180 or > 180))
            throw new JobContactUnavailableException();
        var name = c is null ? null : string.Join(" ", new[] { c.FirstName, c.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
        var address = p is null ? null : string.Join(", ", new[] { p.AddressLine, p.City, p.Postcode }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
        if (address?.Length > 1000) throw new JobContactUnavailableException();
        return new(string.IsNullOrWhiteSpace(address) ? null : address, p?.Latitude, p?.Longitude, string.IsNullOrWhiteSpace(name) ? null : name, string.IsNullOrWhiteSpace(c?.Phone) ? null : c.Phone.Trim());
    }
}
