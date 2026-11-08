using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using Identity.Application.Abstractions.Security;
using Identity.Application.Features.Auth;
using Identity.Infrastructure.Security;
using Microsoft.IdentityModel.Tokens;
using Xunit;
namespace Identity.Tests;
public class ProviderLoginClientTests
{
    [Fact]
    public async Task Internal_lookup_uses_a_subject_bound_short_lived_service_token()
    {
        var user=Guid.NewGuid();var provider=Guid.NewGuid();
        var settings=new JwtSettings{Key=AuthFactory.TestKey,Issuer="identity-tests",Audience="clients"};
        using var client=new HttpClient(new Handler(request=>
        {
            Assert.Equal($"/api/internal/users/{user}/login-access",request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer",request.Headers.Authorization!.Scheme);
            var principal=new JwtSecurityTokenHandler{MapInboundClaims=false}.ValidateToken(request.Headers.Authorization.Parameter,new TokenValidationParameters
            {ValidIssuer=settings.Issuer,ValidAudience="Zumaris.Provider.LoginAccess",IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),ValidateIssuerSigningKey=true,ClockSkew=TimeSpan.Zero},out var token);
            Assert.Equal(user.ToString(),principal.FindFirst("sub")!.Value);Assert.Equal("provider-login-access",principal.FindFirst("scope")!.Value);
            Assert.InRange(token.ValidTo-token.ValidFrom,TimeSpan.FromSeconds(1),TimeSpan.FromSeconds(30));Assert.DoesNotContain(principal.Claims,x=>x.Type=="sid");
            return new(HttpStatusCode.OK){Content=new StringContent($"{{\"payload\":{{\"userId\":\"{user}\",\"providerId\":\"{provider}\",\"isActive\":true,\"hasSignedUp\":true}}}}",Encoding.UTF8,"application/json")};
        })){BaseAddress=new("http://127.0.0.1:5007/")};
        var result=await new ProviderLoginAccessClient(client,settings).GetAsync(user,default);Assert.NotNull(result);Assert.Equal(provider,result.ProviderId);
    }
    [Theory]
    [InlineData(302)][InlineData(401)][InlineData(403)][InlineData(500)]
    public async Task Unexpected_provider_responses_fail_closed(int status)
    {
        using var http=new HttpClient(new Handler(_=>new((HttpStatusCode)status))){BaseAddress=new("http://127.0.0.1:5007/")};
        var error=await Assert.ThrowsAsync<ApplicationLoginException>(()=>new ProviderLoginAccessClient(http,new(){Key=AuthFactory.TestKey,Issuer="tests"}).GetAsync(Guid.NewGuid(),default));Assert.Equal(503,error.Status);
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> handle):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(handle(request));}
}
