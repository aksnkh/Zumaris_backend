using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Zumaris.ServiceRequests.Infrastructure;
using Xunit;
namespace ServiceRequest.Tests;
public class HistoryReferenceDetailsTests
{
    [Fact]
    public async Task Category_name_is_available_even_without_an_icon()
    {
        using var client=new HttpClient(new Handler("{\"id\":1,\"name\":\"Plumbing\",\"iconUrl\":null}"));
        var lookup=new HistoryReferenceLookup(client,new ConfigurationBuilder().Build(),NullLogger<HistoryReferenceLookup>.Instance);
        var category=await lookup.CategoryAsync(1,default);Assert.Equal("Plumbing",category!.Name);Assert.Null(category.IconUrl);
    }
    [Fact]
    public async Task Protected_lookups_forward_the_callers_bearer_and_do_not_expose_wrong_records()
    {
        var id=Guid.NewGuid();var handler=new Handler($"{{\"propertyId\":\"{id}\",\"addressLine\":\"12 Example Road\",\"city\":\"London\",\"postcode\":\"NW11 8HB\",\"countryCode\":\"GB\"}}");
        using var client=new HttpClient(handler);var lookup=new HistoryReferenceLookup(client,new ConfigurationBuilder().Build(),NullLogger<HistoryReferenceLookup>.Instance);
        Assert.Equal("12 Example Road, London, NW11 8HB, GB",await lookup.PropertyAddressAsync(id,"test-bearer",default));
        Assert.Equal("Bearer test-bearer",handler.Authorization);Assert.Equal("https",handler.ForwardedProto);
        Assert.Null(await lookup.PropertyAddressAsync(Guid.NewGuid(),"test-bearer",default));
        handler.Status=HttpStatusCode.Forbidden;Assert.Null(await lookup.PropertyAddressAsync(id,"test-bearer",default));
    }
    [Fact]
    public async Task Area_and_provider_metadata_are_resolved_from_their_own_APIs()
    {
        var handler=new Handler("[{\"id\":1,\"name\":\"Kitchen\"}]");using var client=new HttpClient(handler);
        var lookup=new HistoryReferenceLookup(client,new ConfigurationBuilder().Build(),NullLogger<HistoryReferenceLookup>.Instance);
        Assert.Equal("Kitchen",(await lookup.AreasAsync("test-bearer",default))[1]);
        var id=Guid.NewGuid();handler.Body=$"{{\"providerId\":\"{id}\",\"displayName\":\"Technician\"}}";
        Assert.Equal("Technician",await lookup.ProviderNameAsync(id,"test-bearer",default));
        Assert.EndsWith($"/api/providers/{id}/summary",handler.Path);
    }
    private class Handler(string body):HttpMessageHandler
    {
        public string Body{get;set;}=body;public HttpStatusCode Status{get;set;}=HttpStatusCode.OK;
        public string? Authorization{get;private set;}public string? ForwardedProto{get;private set;}public string? Path{get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Authorization=request.Headers.Authorization?.ToString();ForwardedProto=request.Headers.GetValues("X-Forwarded-Proto").Single();Path=request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(Status){Content=new StringContent(Body,Encoding.UTF8,"application/json")});
        }
    }
}
