using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Zumaris.Billing.Application;
using Xunit;
namespace Billing.Tests;
public class PaymentsTests
{
 [Fact]
 public async Task Swagger_documents_the_overall_payment_stage()
 {using var f=new Factory();using var c=f.Client();var json=await c.GetStringAsync("/swagger/v1/swagger.json");Assert.Contains("/api/payments",json);}

 [Fact]
 public async Task Payment_uses_authoritative_completed_quote_and_never_fakes_a_charge()
 {
  using var f=new Factory();using var c=f.Client();
  var forbidden=await c.PostAsJsonAsync("/api/payments",new{requestId=f.Work.RequestId,amount=1});Assert.Equal(HttpStatusCode.BadRequest,forbidden.StatusCode);
  Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/payments",new{requestId=Guid.Empty})).StatusCode);
  var response=await c.PostAsJsonAsync("/api/payments",new{requestId=f.Work.RequestId});Assert.Equal(HttpStatusCode.OK,response.StatusCode);
  using var json=System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());var p=json.RootElement.GetProperty("payload");
  Assert.Equal(95,p.GetProperty("amount").GetDecimal());Assert.Equal("AwaitingGateway",p.GetProperty("status").GetString());Assert.False(p.GetProperty("canPay").GetBoolean());Assert.Equal(System.Text.Json.JsonValueKind.Null,p.GetProperty("checkoutUrl").ValueKind);
  f.Upstream.Status="InProgress";Assert.Equal(HttpStatusCode.Conflict,(await c.PostAsJsonAsync("/api/payments",new{requestId=f.Work.RequestId})).StatusCode);
  f.Upstream.Status="Completed";f.Upstream.Customer=Guid.NewGuid();Assert.Equal(HttpStatusCode.Conflict,(await c.PostAsJsonAsync("/api/payments",new{requestId=f.Work.RequestId})).StatusCode);
  using var anonymous=f.Client(false);Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.PostAsJsonAsync("/api/payments",new{requestId=f.Work.RequestId})).StatusCode);
  f.Session.Active=false;Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/payments/"+Guid.NewGuid())).StatusCode);
 }
 [Theory][InlineData("Tech")][InlineData("Admin")]
 public async Task Non_customer_tokens_cannot_create_or_read_customer_payments(string application)
 {
  using var f=new Factory();using var c=f.Client(application:application);
  Assert.Equal(HttpStatusCode.Forbidden,(await c.PostAsync("/api/payments",null)).StatusCode);
  Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/payments/"+Guid.NewGuid())).StatusCode);
 }
 private sealed class Factory:WebApplicationFactory<Program>
 {
  const string Key="test-only-billing-signing-key-123456789012345";public readonly Guid User=Guid.NewGuid();public CompletedWork Work{get;}
  public readonly Sessions Session=new();public readonly Upstream Upstream;
  public Factory(){Work=new(Guid.NewGuid(),User,Guid.NewGuid(),"Completed",Guid.NewGuid(),95,"GBP");Upstream=new(Work);}
  protected override void ConfigureWebHost(IWebHostBuilder b)
  {
   b.UseEnvironment("Development");b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"Jwt:Key",Key},{"Jwt:Issuer","tests"},{"Jwt:Audience","clients"}}));
   b.ConfigureServices(s=>{s.RemoveAll<IPaymentRepository>();s.AddSingleton<IPaymentRepository,MemoryPayments>();s.RemoveAll<ICompletedWorkClient>();s.AddSingleton<ICompletedWorkClient>(Upstream);s.RemoveAll<IIdentitySessionValidator>();s.AddSingleton<IIdentitySessionValidator>(Session);});
  }
  public HttpClient Client(bool auth=true,string application="App"){var c=CreateClient();if(auth){var t=new JwtSecurityToken("tests","clients",new[]{new Claim("sub",User.ToString()),new Claim("sid",Guid.NewGuid().ToString()),new Claim("application",application),new Claim("role",application=="Tech"?"Provider":application=="Admin"?"Admin":"Customer"),new Claim("providerId",Guid.NewGuid().ToString())},DateTime.UtcNow.AddSeconds(-1),DateTime.UtcNow.AddMinutes(5),new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),SecurityAlgorithms.HmacSha256));c.DefaultRequestHeaders.Authorization=new("Bearer",new JwtSecurityTokenHandler().WriteToken(t));}return c;}
 }
 private class Sessions:IIdentitySessionValidator{public bool Active=true;public Task<bool> IsActiveAsync(string token,bool https,CancellationToken ct)=>Task.FromResult(Active);}
 private class Upstream(CompletedWork work):ICompletedWorkClient
 {public string Status=work.Status;public Guid Customer=work.CustomerId;public Task<CompletedWork> GetAsync(Guid actor,Guid id,string b,CancellationToken ct)=>Task.FromResult(work with{Status=Status,CustomerId=Customer});}
 private class MemoryPayments:IPaymentRepository
 {
  public Task<PaymentIntent> CreateAsync(Guid actor,CompletedWork w,CancellationToken ct)=>Task.FromResult(new PaymentIntent(Guid.NewGuid(),w.RequestId,w.QuoteId,w.Amount,w.CurrencyCode,"PayPal","AwaitingGateway",DateTime.UtcNow));
  public Task<PaymentIntent?> GetAsync(Guid actor,Guid id,CancellationToken ct)=>Task.FromResult<PaymentIntent?>(null);
 }
}
