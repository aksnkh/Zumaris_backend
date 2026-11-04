using System.Net;
using System.Net.Http.Json;
using Zumaris.Provider.Application;
using Zumaris.Provider.Infrastructure.Integrations;
using Xunit;
namespace Provider.Tests;
public class ProviderSignupClientTests
{
 [Fact]
 public async Task Identity_profile_is_read_from_its_envelope_and_bound_to_the_bearer_identity()
 {
  var actor=Guid.NewGuid();var handler=new Handler(_=>new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{payload=new{userId=actor,firstName="John",lastName="Smith",phone="+447700900123"}})});var client=new ProviderSignupClient(new Clients(handler));
  Assert.Equal(actor,(await client.IdentityAsync(actor,"valid-token",default)).UserId);Assert.Equal("/api/profile/me",handler.Path);Assert.Equal("Bearer valid-token",handler.Authorization);
  Assert.Equal(503,(await Assert.ThrowsAsync<ProviderWorkflowException>(()=>client.IdentityAsync(Guid.NewGuid(),"valid-token",default))).Status);
 }
 [Theory]
 [InlineData(404,400)][InlineData(401,401)][InlineData(503,503)][InlineData(302,503)]
 public async Task Invalid_reference_responses_cannot_complete_signup(int code,int expected)
 {
  var client=new ProviderSignupClient(new Clients(new Handler(_=>new HttpResponseMessage((HttpStatusCode)code))));Assert.Equal(expected,(await Assert.ThrowsAsync<ProviderWorkflowException>(()=>client.IdentityAsync(Guid.NewGuid(),"valid-token",default))).Status);
 }
 private class Clients(Handler handler):IHttpClientFactory
 {public HttpClient CreateClient(string name)=>new(handler,false){BaseAddress=new Uri("https://identity.test/")};}
 private class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
 {public string? Path,Authorization;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Path=r.RequestUri!.AbsolutePath;Authorization=r.Headers.Authorization?.ToString();return Task.FromResult(response(r));}}
}
