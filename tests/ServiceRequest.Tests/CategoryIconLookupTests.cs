using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Zumaris.ServiceRequests.Infrastructure;
using Xunit;
namespace ServiceRequest.Tests;

public class CategoryIconLookupTests
{
    [Theory]
    [InlineData(200,"{\"id\":1,\"iconUrl\":\"/service-catalog/api/categories/icons/1.svg\"}","/service-catalog/api/categories/icons/1.svg")]
    [InlineData(200,"{\"id\":1,\"iconUrl\":null}",null)]
    [InlineData(200,"{\"id\":2,\"iconUrl\":\"/wrong.png\"}",null)]
    [InlineData(200,"not json",null)]
    [InlineData(404,"",null)]
    [InlineData(503,"",null)]
    public async Task Uses_actual_catalog_icon_and_tolerates_unavailable_or_missing_metadata(int code,string body,string? expected)
    {
        var handler = new Handler(code,body);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5006/") };
        var lookup = new CategoryIconLookup(client,NullLogger<CategoryIconLookup>.Instance);
        Assert.Equal(expected,await lookup.GetIconUrlAsync(1,default));
        Assert.Equal("http://127.0.0.1:5006/api/categories/1",handler.Url);
    }
    private class Handler(int code,string body) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Url=request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) {
                Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json") });
        }
    }
}
