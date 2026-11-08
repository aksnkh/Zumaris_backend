using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Identity.Application.Features.Auth;
using Microsoft.IdentityModel.Tokens;
using Xunit;
namespace Identity.Tests;
public sealed class ApplicationLoginTests
{
    private static Task<HttpResponseMessage> Login(HttpClient c,string app)=>c.PostAsJsonAsync("/api/auth/login",new{userName="alice",password="Password-for-tests-1",application=app});
    private static async Task<(string Access,string Refresh)> Read(HttpResponseMessage response,string app)
    {
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var payload=json.RootElement.GetProperty("payload");Assert.Equal(app,payload.GetProperty("application").GetString());
        var cookie=response.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("zumaris_refresh_token_"+app.ToLowerInvariant()+"="));
        return (payload.GetProperty("accessToken").GetString()!,cookie.Split(';')[0].Split('=',2)[1]);
    }
    private static Task<HttpResponseMessage> Refresh(HttpClient c,string app,string refresh)
    {
        var request=new HttpRequestMessage(HttpMethod.Post,"/api/auth/refresh?application="+app);
        request.Headers.Add("Cookie","zumaris_refresh_token_"+app.ToLowerInvariant()+"="+refresh);return c.SendAsync(request);
    }
    [Theory]
    [InlineData(null)][InlineData("")][InlineData("Unknown")][InlineData("tech")]
    public async Task Login_requires_an_explicit_valid_application(string? app)
    {
        using var f=new AuthFactory();using var c=f.NewClient();
        Assert.Equal(HttpStatusCode.BadRequest,(await Login(c,app!)).StatusCode);Assert.Empty(f.Sessions.Items);
    }
    [Fact]
    public async Task Customer_cannot_obtain_tech_or_admin_tokens_or_assign_claims_to_itself()
    {
        using var f=new AuthFactory();using var c=f.NewClient();
        foreach(var app in new[]{"Tech","Admin"})
        {
            var response=await c.PostAsJsonAsync("/api/auth/login",new{userName="alice",password="Password-for-tests-1",application=app,role="Admin",providerId=Guid.NewGuid()});
            Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);Assert.False(response.Headers.Contains("Set-Cookie"));
        }
        Assert.Empty(f.Sessions.Items);
        var login=await Read(await Login(c,"App"),"App");var jwt=new JwtSecurityTokenHandler().ReadJwtToken(login.Access);
        Assert.Equal("Customer",jwt.Claims.Single(x=>x.Type=="role").Value);Assert.DoesNotContain(jwt.Claims,x=>x.Type=="providerId");
    }
    [Theory]
    [InlineData(false,true)][InlineData(true,false)]
    public async Task Inactive_or_incomplete_provider_cannot_sign_in(bool active,bool signup)
    {
        using var f=new AuthFactory();using var c=f.NewClient();var user=f.Users.User.Id;
        f.ApplicationProviders.Items[user]=new(user,Guid.NewGuid(),active,signup);
        Assert.Equal(HttpStatusCode.Forbidden,(await Login(c,"Tech")).StatusCode);Assert.Empty(f.Sessions.Items);
    }
    [Fact]
    public async Task Sessions_cookies_refresh_and_revocation_stay_within_their_application()
    {
        using var f=new AuthFactory();using var c=f.NewClient();var user=f.Users.User.Id;var provider=Guid.NewGuid();
        f.ApplicationProviders.Items[user]=new(user,provider,true,true);
        var app=await Read(await Login(c,"App"),"App");var tech=await Read(await Login(c,"Tech"),"Tech");
        var jwt=new JwtSecurityTokenHandler().ReadJwtToken(tech.Access);Assert.Equal(provider.ToString(),jwt.Claims.Single(x=>x.Type=="providerId").Value);
        Assert.Equal("Provider",jwt.Claims.Single(x=>x.Type=="role").Value);
        Assert.Equal(HttpStatusCode.Forbidden,(await Refresh(c,"Tech",app.Refresh)).StatusCode);
        var nextTech=await Read(await Refresh(c,"Tech",tech.Refresh),"Tech");Assert.Equal(provider.ToString(),new JwtSecurityTokenHandler().ReadJwtToken(nextTech.Access).Claims.Single(x=>x.Type=="providerId").Value);
        c.DefaultRequestHeaders.Authorization=new("Bearer",app.Access);
        using var sessions=JsonDocument.Parse(await c.GetStringAsync("/api/auth/sessions"));Assert.Single(sessions.RootElement.EnumerateArray());
        var techSession=new JwtSecurityTokenHandler().ReadJwtToken(tech.Access).Claims.Single(x=>x.Type=="sid").Value;
        Assert.Equal(HttpStatusCode.NoContent,(await c.DeleteAsync("/api/auth/sessions/"+techSession)).StatusCode);
        Assert.Null(f.Sessions.Items[Guid.Parse(techSession)].RevokedAtUtc);
        Assert.Equal(HttpStatusCode.Forbidden,(await Refresh(c,"Tech",nextTech.Refresh)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await c.DeleteAsync("/api/auth/sessions")).StatusCode);
        Assert.Null(f.Sessions.Items[Guid.Parse(techSession)].RevokedAtUtc);
        c.DefaultRequestHeaders.Authorization=null;
        await Read(await Refresh(c,"Tech",nextTech.Refresh),"Tech");
        Assert.Equal(HttpStatusCode.Unauthorized,(await Refresh(c,"App",app.Refresh)).StatusCode);
    }
    [Fact]
    public async Task Provider_unavailable_fails_closed_without_creating_a_session()
    {
        using var f=new AuthFactory();using var c=f.NewClient();f.ApplicationProviders.Unavailable=true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await Login(c,"Tech")).StatusCode);Assert.Empty(f.Sessions.Items);
        await Read(await Login(c,"App"),"App");
    }
    [Fact]
    public async Task Refresh_rechecks_provider_and_admin_membership()
    {
        using var f=new AuthFactory();using var c=f.NewClient();var user=f.Users.User.Id;
        f.ApplicationProviders.Items[user]=new(user,Guid.NewGuid(),true,true);f.ApplicationAdmins.Users.Add(user);
        var tech=await Read(await Login(c,"Tech"),"Tech");var admin=await Read(await Login(c,"Admin"),"Admin");
        f.ApplicationProviders.Items[user]=f.ApplicationProviders.Items[user] with{IsActive=false};f.ApplicationAdmins.Users.Remove(user);
        Assert.Equal(HttpStatusCode.Forbidden,(await Refresh(c,"Tech",tech.Refresh)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Refresh(c,"Admin",admin.Refresh)).StatusCode);
        Assert.All(f.Sessions.Items.Values,s=>Assert.NotNull(s.RevokedAtUtc));
    }
    [Fact]
    public async Task Even_a_signed_claim_change_cannot_change_the_persisted_session_application()
    {
        using var f=new AuthFactory();using var c=f.NewClient();var app=await Read(await Login(c,"App"),"App");
        var original=new JwtSecurityTokenHandler().ReadJwtToken(app.Access);
        var claims=original.Claims.Where(x=>x.Type is not ("application" or "role" or "providerId")).ToList();
        claims.AddRange([new Claim("application","Tech"),new Claim("role","Provider"),new Claim("providerId",Guid.NewGuid().ToString())]);
        var altered=new JwtSecurityToken("identity-tests","identity-test-clients",claims,DateTime.UtcNow.AddSeconds(-1),DateTime.UtcNow.AddMinutes(5),new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthFactory.TestKey)),SecurityAlgorithms.HmacSha256));
        c.DefaultRequestHeaders.Authorization=new("Bearer",new JwtSecurityTokenHandler().WriteToken(altered));
        Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/auth/session")).StatusCode);
    }
}
