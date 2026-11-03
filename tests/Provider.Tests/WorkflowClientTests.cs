using System.Net;
using System.Net.Http.Json;
using Zumaris.Provider.Application;
using Zumaris.Provider.Infrastructure.Integrations;
using Xunit;
namespace Provider.Tests;
public class WorkflowClientTests
{
 [Theory]
 [InlineData(204,true)]
 [InlineData(403,false)]
 [InlineData(401,false)]
 public async Task Admin_check_uses_identity_not_caller_supplied_profile_fields(int status,bool allowed)
 {
  var factory=new Clients(new Handler(_=>new HttpResponseMessage((HttpStatusCode)status)));var client=new ProviderWorkflowClient(factory);
  Assert.Equal(allowed,await client.IsAdminAsync("test-bearer",default));Assert.Equal("/api/auth/session/admin-access",factory.Handler.Path);Assert.Equal("Bearer test-bearer",factory.Handler.Authorization);
 }
 [Theory]
 [InlineData(200)]
 [InlineData(302)]
 [InlineData(404)]
 [InlineData(503)]
 public async Task Unexpected_identity_responses_never_grant_admin_access(int status)
 {
  var client=new ProviderWorkflowClient(new Clients(new Handler(_=>new HttpResponseMessage((HttpStatusCode)status))));var error=await Assert.ThrowsAsync<ProviderWorkflowException>(()=>client.IsAdminAsync("test",default));Assert.Equal(503,error.Status);
 }
 [Fact]
 public async Task Wrong_completed_job_response_is_rejected()
 {
  var work=new CompletedWork(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Completed",Guid.NewGuid(),95,"GBP");var client=new ProviderWorkflowClient(new Clients(new Handler(_=>new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(work)})));
  var error=await Assert.ThrowsAsync<ProviderWorkflowException>(()=>client.CompletedWorkAsync(Guid.NewGuid(),"test",default));Assert.Equal(503,error.Status);
 }
 private class Clients(Handler handler):IHttpClientFactory
 {public Handler Handler=handler;public HttpClient CreateClient(string name)=>new(Handler,false){BaseAddress=new Uri("https://reference.test/")};}
 private class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
 {public string? Path,Authorization;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Path=r.RequestUri!.AbsolutePath;Authorization=r.Headers.Authorization?.ToString();return Task.FromResult(response(r));}}
}
