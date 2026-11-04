using System.Net;
using Xunit;
namespace Identity.Tests;
public class CorsTests
{
 [Theory]
 [InlineData("https://tech.zumaris.com",true)]
 [InlineData("https://app.zumaris.com",true)]
 [InlineData("https://untrusted.example",false)]
 public async Task Signup_preflight_allows_only_configured_frontends_with_credentials(string origin,bool allowed)
 {
  using var f=new AuthFactory();using var c=f.NewClient();using var request=new HttpRequestMessage(HttpMethod.Options,"/api/auth/signup");
  request.Headers.Add("Origin",origin);request.Headers.Add("Access-Control-Request-Method","POST");request.Headers.Add("Access-Control-Request-Headers","Content-Type,Authorization,X-Zumaris-CSRF");
  using var response=await c.SendAsync(request);Assert.Equal(HttpStatusCode.NoContent,response.StatusCode);
  if(!allowed){Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));return;}
  Assert.Equal(origin,Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));Assert.Equal("true",Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
  var headers=string.Join(",",response.Headers.GetValues("Access-Control-Allow-Headers"));foreach(var name in new[]{"Content-Type","Authorization","X-Zumaris-CSRF"})Assert.Contains(name,headers,StringComparison.OrdinalIgnoreCase);
 }
}
