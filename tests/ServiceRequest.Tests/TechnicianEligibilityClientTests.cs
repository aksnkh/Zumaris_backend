using System.Net;
using System.Net.Http.Json;
using Zumaris.ServiceRequests.Application;
using Zumaris.ServiceRequests.Application.Quotes;
using Zumaris.ServiceRequests.Infrastructure;
using Xunit;
namespace ServiceRequest.Tests;
public class TechnicianEligibilityClientTests
{
 [Fact]
 public async Task Eligibility_is_bound_to_current_identity_and_fails_closed_for_unavailable_reference()
 {
  var actor=Guid.NewGuid();var p=new TechnicianEligibility(Guid.NewGuid(),actor,new[]{1,2});
  using var good=new HttpClient(new Handler(_=>new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(p)})){BaseAddress=new Uri("https://provider.test/")};
  Assert.Equal(actor,(await new TechnicianEligibilityClient(good).GetAsync(actor,"test",default)).UserId);
  await Assert.ThrowsAsync<QuoteUnavailableException>(()=>new TechnicianEligibilityClient(good).GetAsync(Guid.NewGuid(),"test",default));
  using var denied=new HttpClient(new Handler(_=>new HttpResponseMessage(HttpStatusCode.Forbidden))){BaseAddress=good.BaseAddress};
  await Assert.ThrowsAsync<QuoteForbiddenException>(()=>new TechnicianEligibilityClient(denied).GetAsync(actor,"test",default));
  using var bad=new HttpClient(new Handler(_=>new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))){BaseAddress=good.BaseAddress};
  await Assert.ThrowsAsync<QuoteUnavailableException>(()=>new TechnicianEligibilityClient(bad).GetAsync(actor,"test",default));
 }
 private class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
 {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>Task.FromResult(response(r));}
}
