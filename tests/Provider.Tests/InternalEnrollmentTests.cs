using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using Zumaris.Provider.Application;
namespace Provider.Tests;
public class InternalEnrollmentTests
{
 [Fact]
 public async Task Identity_can_enroll_its_signed_Tech_account_without_calling_back_to_Identity()
 {
  using var f=new Factory();using var c=f.CreateClient();c.DefaultRequestHeaders.Authorization=new("Bearer",f.Token("provider-enrollment","Tech"));
  var first=await c.PostAsJsonAsync($"/api/internal/users/{f.User}/signup",new{firstName="John",lastName="Smith",phone="+447700900123",acceptTermsAndPrivacy=true});Assert.Equal(HttpStatusCode.OK,first.StatusCode);Assert.Equal(1,f.Repo.Calls);Assert.Equal("John Smith",f.Repo.Name);Assert.Equal(f.User,f.Repo.User);Assert.Equal(0,f.Sessions.Calls);
  Assert.Equal(HttpStatusCode.Forbidden,(await c.PostAsJsonAsync($"/api/internal/users/{Guid.NewGuid()}/signup",new{firstName="John",lastName="Smith",phone="+447700900123",acceptTermsAndPrivacy=true})).StatusCode);Assert.Equal(1,f.Repo.Calls);
  Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync($"/api/internal/users/{f.User}/signup",new{firstName="John",lastName="Smith",phone="+447700900123",acceptTermsAndPrivacy=false})).StatusCode);
 }
 [Theory][InlineData("provider-login-access","Tech")][InlineData("provider-enrollment","App")]
 public async Task Lookup_or_wrong_application_credentials_cannot_enroll_an_account(string scope,string app)
 {
  using var f=new Factory();using var c=f.CreateClient();c.DefaultRequestHeaders.Authorization=new("Bearer",f.Token(scope,app));
  Assert.Equal(HttpStatusCode.Forbidden,(await c.PostAsJsonAsync($"/api/internal/users/{f.User}/signup",new{firstName="John",lastName="Smith",phone="+447700900123",acceptTermsAndPrivacy=true})).StatusCode);Assert.Equal(0,f.Repo.Calls);
 }
 private sealed class Factory:WebApplicationFactory<Program>
 {
  private const string Key="independent-provider-enrollment-test-key-123456789012345";
  public Guid User=Guid.NewGuid();public Repository Repo=new();public Sessions Sessions=new();
  protected override void ConfigureWebHost(IWebHostBuilder b)
  {b.UseEnvironment("Development");b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"Jwt:Key",Key},{"Jwt:Issuer","tests"},{"Jwt:Audience","clients"}}));b.ConfigureServices(s=>{s.RemoveAll<IProviderSignupRepository>();s.AddSingleton<IProviderSignupRepository>(Repo);s.RemoveAll<IIdentitySessionValidator>();s.AddSingleton<IIdentitySessionValidator>(Sessions);});}
  public string Token(string scope,string app)=>new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("tests","Zumaris.Provider.LoginAccess",new Claim[]{new("sub",User.ToString()),new("scope",scope),new("application",app)},DateTime.UtcNow,DateTime.UtcNow.AddSeconds(30),new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),SecurityAlgorithms.HmacSha256)));
 }
 private sealed class Repository:IProviderSignupRepository
 {public int Calls;public string? Name;public Guid User;public Task<ProviderSignupResult> CompleteAsync(Guid user,string name,string? terms,string? privacy,CancellationToken ct){Calls++;Name=name;User=user;return Task.FromResult(new ProviderSignupResult(new(Guid.NewGuid(),user,name,true,false,"Draft",1,null,[],[]),DateTime.UtcNow,terms,privacy));}}
 private sealed class Sessions:IIdentitySessionValidator
 {public int Calls;public Task<bool> IsActiveAsync(string token,bool https,CancellationToken ct){Calls++;return Task.FromResult(true);}}
}
