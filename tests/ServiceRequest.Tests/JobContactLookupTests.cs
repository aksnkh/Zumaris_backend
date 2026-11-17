using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Zumaris.ServiceRequests.Application;
using Zumaris.ServiceRequests.Infrastructure;
using Xunit;
namespace ServiceRequest.Tests;

public sealed class JobContactLookupTests
{
    [Fact]
    public async Task Reads_request_property_and_requesting_customer_profile_using_the_customer_bearer()
    {
        var customer = Guid.NewGuid(); var property = Guid.NewGuid();
        var handler = new Handler(customer, property);
        var snapshot = await Create(handler).Get(customer, property, "customer-token", default);
        Assert.Equal("12 Example Road, London, NW11 8HB", snapshot.PropertyAddress);
        Assert.Equal(51.556m, snapshot.Latitude); Assert.Equal(-.192m, snapshot.Longitude);
        Assert.Equal("Alex Customer", snapshot.CustomerName); Assert.Equal("+447700900123", snapshot.CustomerPhone);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("Bearer customer-token", r.Auth));
        Assert.Contains(handler.Requests, r => r.Url == "https://identity.example.test/api/profile/me");
        Assert.Contains(handler.Requests, r => r.Url == $"https://property.example.test/api/properties/{property}");
    }
    [Fact]
    public async Task Missing_fields_remain_null_and_never_fall_back_to_username_or_email()
    {
        var customer = Guid.NewGuid(); var property = Guid.NewGuid();
        var handler = new Handler(customer, property) { MissingFields = true };
        var result = await Create(handler).Get(customer, property, "customer-token", default);
        Assert.Null(result.CustomerName); Assert.Null(result.CustomerPhone);
        Assert.Null(result.Latitude); Assert.Null(result.Longitude);
        Assert.DoesNotContain("example.test", JsonSerializer.Serialize(result));
    }
    [Fact]
    public async Task Missing_resources_do_not_generate_fabricated_contact_or_location_values()
    {
        var result = await Create(new Handler(Guid.NewGuid(), Guid.NewGuid()) { Status = HttpStatusCode.NotFound }).Get(Guid.NewGuid(), Guid.NewGuid(), "token", default);
        Assert.Equal(new JobContactSnapshot(null, null, null, null, null), result);
    }
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_failed_source_prevents_acceptance_from_freezing_a_missing_snapshot(HttpStatusCode status)
    {
        var customer = Guid.NewGuid(); var property = Guid.NewGuid();
        await Assert.ThrowsAsync<JobContactUnavailableException>(() => Create(new Handler(customer, property) { Status = status }).Get(customer, property, "token", default));
    }
    [Fact]
    public async Task Wrong_customer_or_property_is_rejected()
    {
        var handler = new Handler(Guid.NewGuid(), Guid.NewGuid());
        await Assert.ThrowsAsync<JobContactUnavailableException>(() => Create(handler).Get(Guid.NewGuid(), Guid.NewGuid(), "token", default));
    }
    private static JobContactLookup Create(Handler handler) => new(new Factory(handler), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Identity:BaseUrl"] = "https://identity.example.test/", ["Property:BaseUrl"] = "https://property.example.test/" }).Build());
    private sealed class Factory(Handler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class Handler(Guid customer, Guid property) : HttpMessageHandler
    {
        public bool MissingFields;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public List<(string Url, string? Auth)> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString()));
            object data = request.RequestUri.Host == "identity.example.test"
                ? new { payload = new { userId = customer, firstName = MissingFields ? null : "Alex", lastName = MissingFields ? null : "Customer", phone = MissingFields ? null : "+447700900123", userName = "private@example.test", email = "private@example.test" } }
                : new { propertyId = property, addressLine = "12 Example Road", city = "London", postcode = "NW11 8HB", latitude = MissingFields ? (decimal?)null : 51.556m, longitude = MissingFields ? (decimal?)null : -.192m };
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") });
        }
    }
}
