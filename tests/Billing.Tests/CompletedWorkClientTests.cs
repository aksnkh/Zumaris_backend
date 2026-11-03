using System.Net;
using System.Net.Http.Json;
using Zumaris.Billing.Application;
using Zumaris.Billing.Infrastructure;
using Xunit;
namespace Billing.Tests;
public class CompletedWorkClientTests
{
 [Fact]
 public async Task Reference_ownership_and_unavailability_cannot_be_used_to_create_a_payment()
 {
  var user=Guid.NewGuid();var work=new CompletedWork(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Completed",Guid.NewGuid(),95,"GBP");
  using var wrong=new HttpClient(new Handler(_=>new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(work)})){BaseAddress=new Uri("https://request.test/")};
  var error=await Assert.ThrowsAsync<BillingException>(()=>new CompletedWorkClient(wrong).GetAsync(user,work.RequestId,"test",default));Assert.Equal(503,error.Status);
  using var denied=new HttpClient(new Handler(_=>new HttpResponseMessage(HttpStatusCode.NotFound))){BaseAddress=wrong.BaseAddress};
  error=await Assert.ThrowsAsync<BillingException>(()=>new CompletedWorkClient(denied).GetAsync(user,work.RequestId,"test",default));Assert.Equal(404,error.Status);
  using var notDone=new HttpClient(new Handler(_=>new HttpResponseMessage(HttpStatusCode.Conflict))){BaseAddress=wrong.BaseAddress};
  error=await Assert.ThrowsAsync<BillingException>(()=>new CompletedWorkClient(notDone).GetAsync(user,work.RequestId,"test",default));Assert.Equal(409,error.Status);
 }
 private class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
 {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>Task.FromResult(response(r));}
}
