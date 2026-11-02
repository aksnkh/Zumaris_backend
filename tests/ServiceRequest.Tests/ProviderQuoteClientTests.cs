using System.Net;
using System.Net.Http.Json;
using Zumaris.ServiceRequests.Application.Quotes;
using Zumaris.ServiceRequests.Infrastructure;
using Xunit;
namespace ServiceRequest.Tests;
public class ProviderQuoteClientTests
{
    [Theory]
    [InlineData(404,true)]
    [InlineData(403,true)]
    [InlineData(401,false)]
    [InlineData(500,false)]
    [InlineData(503,false)]
    public async Task Provider_errors_fail_closed_and_bearer_is_forwarded(int status,bool forbidden)
    {
        var handler=new Handler(status);using var client=new HttpClient(handler){BaseAddress=new Uri("http://127.0.0.1:5007/")};
        var service=new ProviderQuoteEligibility(client);
        if(forbidden)await Assert.ThrowsAsync<QuoteForbiddenException>(()=>service.GetAsync(Guid.NewGuid(),1,"test-bearer",default));
        else await Assert.ThrowsAsync<QuoteUnavailableException>(()=>service.GetAsync(Guid.NewGuid(),1,"test-bearer",default));
        Assert.Equal("Bearer test-bearer",handler.Authorization);
        Assert.Equal("/api/providers/me/quote-profile?categoryId=1",handler.Path);
    }
    [Fact]
    public async Task Provider_profile_must_match_the_authenticated_user_and_category()
    {
        var handler=new Handler(200);using var client=new HttpClient(handler){BaseAddress=new Uri("http://127.0.0.1:5007/")};
        await Assert.ThrowsAsync<QuoteForbiddenException>(()=>new ProviderQuoteEligibility(client).GetAsync(Guid.NewGuid(),1,"test-bearer",default));
    }
    private class Handler(int status):HttpMessageHandler
    {
        public string? Authorization{get;private set;}public string? Path{get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Authorization=request.Headers.Authorization?.ToString();Path=request.RequestUri!.PathAndQuery;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status){Content=JsonContent.Create(new ProviderQuoteIdentity(Guid.NewGuid(),Guid.NewGuid(),"Verified",null,null,null,true,2))});
        }
    }
}
