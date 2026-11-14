using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
namespace Zumaris.BuildingBlocks.Common.Passport;
// Forward the caller's token; remote services independently enforce resource ownership.
public sealed class PassportHttpClient(IHttpClientFactory factory, IHttpContextAccessor accessor, IConfiguration config)
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public async Task<T> GetAsync<T>(string service, string path, CancellationToken ct)
    {
        var uri = new Uri(config[service + ":BaseUrl"] ?? service switch { "Property" => "http://127.0.0.1:5003/", "Maintenance" => "http://127.0.0.1:5005/", "Document" => "http://127.0.0.1:5004/", "ServiceRequest" => "http://127.0.0.1:5002/", _ => throw new InvalidOperationException("Unknown service.") });
        if (!uri.IsAbsoluteUri || !string.IsNullOrEmpty(uri.UserInfo) || !uri.AbsoluteUri.EndsWith('/') || !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback)) throw new InvalidOperationException("Service URLs must use HTTPS or loopback HTTP and a trailing slash.");
        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(uri, path));
        req.Headers.TryAddWithoutValidation("Authorization", accessor.HttpContext!.Request.Headers.Authorization.ToString());
        if (uri.IsLoopback) req.Headers.Add("X-Forwarded-Proto", "https");
        try
        {
            using var response = await factory.CreateClient("passport").SendAsync(req, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) throw new PassportException(404, "Property or related record not found.");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) throw new PassportException(403, "Access to the related resource was denied.");
            if (!response.IsSuccessStatusCode) throw new PassportException(503, "A required service is temporarily unavailable.");
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken: ct) ?? throw new PassportException(503, "A required service returned an invalid response.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { throw new PassportException(503, "A required service is temporarily unavailable."); }
    }
    public async Task RequireProperty(Guid id, bool write, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new PassportException(400, "A propertyId is required.");
        var r = await GetAsync<Zumaris.BuildingBlocks.Common.Responses.Response<PropertyPermission>>("Property", $"api/properties/{id}/access", ct);
        if (r.Payload.PropertyId != id) throw new PassportException(503, "Property access could not be verified.");
        if (write && !r.Payload.CanWrite) throw new PassportException(403, "Only the property creator can modify Passport records.");
    }
}
