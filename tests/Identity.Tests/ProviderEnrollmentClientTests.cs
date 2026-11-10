using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using Identity.Application.Features.Auth;
using Identity.Infrastructure.Security;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using Zumaris.Identity.Domain.Entities;
namespace Identity.Tests;
public class ProviderEnrollmentClientTests
{
 [Fact]
 public async Task Enrollment_uses_a_short_purpose_bound_Tech_credential_and_validates_the_provider_identity()
 {
  var user=new User{Id=Guid.NewGuid(),Application="Tech",FirstName="John",LastName="Smith",Phone="+447700900123",ProviderEnrollmentRequestedAtUtc=DateTime.UtcNow};var provider=Guid.NewGuid();
  using var http=new HttpClient(new Handler(request=>
  {
   Assert.Equal(HttpMethod.Post,request.Method);Assert.Equal($"/api/internal/users/{user.Id}/signup",request.RequestUri!.AbsolutePath);
   var p=new JwtSecurityTokenHandler{MapInboundClaims=false}.ValidateToken(request.Headers.Authorization!.Parameter,new TokenValidationParameters{ValidIssuer="tests",ValidAudience="Zumaris.Provider.LoginAccess",IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthFactory.TestKey)),ValidateIssuerSigningKey=true},out var token);
   Assert.Equal(user.Id.ToString(),p.FindFirst("sub")!.Value);Assert.Equal("provider-enrollment",p.FindFirst("scope")!.Value);Assert.Equal("Tech",p.FindFirst("application")!.Value);Assert.InRange(token.ValidTo-token.ValidFrom,TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(30));
   return new(HttpStatusCode.OK){Content=new StringContent($"{{\"payload\":{{\"profile\":{{\"userId\":\"{user.Id}\",\"providerId\":\"{provider}\",\"isActive\":true}}}}}}",Encoding.UTF8,"application/json")};
  })){BaseAddress=new("http://127.0.0.1:5007/")};
  var client=new ProviderEnrollmentClient(http,new(){Key=AuthFactory.TestKey,Issuer="tests"});Assert.Equal(provider,(await client.EnrollAsync(user,default)).ProviderId);
  user.Application="App";Assert.Equal(403,(await Assert.ThrowsAsync<ApplicationLoginException>(()=>client.EnrollAsync(user,default))).Status);
 }
 private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> action):HttpMessageHandler
 {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(action(request));}
}
