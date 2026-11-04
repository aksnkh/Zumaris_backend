using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace Provider.Tests;
public class CorsTests
{
 [Theory]
 [InlineData("https://tech.zumaris.com",true)]
 [InlineData("https://app.zumaris.com",true)]
 [InlineData("https://admin.zumaris.com",true)]
 [InlineData("https://untrusted.example",false)]
 public async Task Signup_preflight_allows_only_configured_frontends_with_credentials(string origin,bool allowed)
 {
  using var f=new Factory();using var c=f.CreateClient();using var request=new HttpRequestMessage(HttpMethod.Options,"/api/providers/signup");
  request.Headers.Add("Origin",origin);request.Headers.Add("Access-Control-Request-Method","POST");request.Headers.Add("Access-Control-Request-Headers","Content-Type,Authorization,X-Zumaris-CSRF");
  using var response=await c.SendAsync(request);Assert.Equal(HttpStatusCode.NoContent,response.StatusCode);
  if(!allowed){Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));return;}
  Assert.Equal(origin,Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));Assert.Equal("true",Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
  var headers=string.Join(",",response.Headers.GetValues("Access-Control-Allow-Headers"));foreach(var name in new[]{"Content-Type","Authorization","X-Zumaris-CSRF"})Assert.Contains(name,headers,StringComparison.OrdinalIgnoreCase);
 }
 private class Factory:WebApplicationFactory<Program>
 {
  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
   builder.UseEnvironment("Development");builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"Jwt:Key","cors-test-only-key-12345678901234567890"},{"Jwt:Issuer","tests"},{"Jwt:Audience","clients"}}));
  }
 }
}
