using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Zumaris.Provider.Application;
using Xunit;
namespace Provider.Tests;
public class ProviderSignupTests
{
 [Fact]
 public async Task Signup_requires_affirmative_consent_and_creates_a_draft_without_trade_or_verification()
 {
  using var f=new Factory();using var c=f.Client();
  var falseConsent=await c.PostAsJsonAsync("/api/providers/signup",new{acceptTermsAndPrivacy=false});Assert.Equal(HttpStatusCode.BadRequest,falseConsent.StatusCode);Assert.Equal(0,f.Repository.Calls);
  var missing=await c.PostAsJsonAsync("/api/providers/signup",new{});Assert.Equal(HttpStatusCode.BadRequest,missing.StatusCode);using var invalid=JsonDocument.Parse(await missing.Content.ReadAsStringAsync());Assert.Equal(400,invalid.RootElement.GetProperty("statusCode").GetInt32());Assert.True(invalid.RootElement.TryGetProperty("errors",out _));
  var response=await c.PostAsJsonAsync("/api/providers/signup",new{acceptTermsAndPrivacy=true});Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var profile=json.RootElement.GetProperty("payload").GetProperty("profile");
  Assert.False(profile.GetProperty("isVerified").GetBoolean());Assert.Equal("Draft",profile.GetProperty("onboardingStatus").GetString());Assert.Equal(0,profile.GetProperty("categoryIds").GetArrayLength());Assert.Equal(f.User,profile.GetProperty("userId").GetGuid());Assert.Equal("John Smith",profile.GetProperty("displayName").GetString());Assert.False(json.RootElement.GetProperty("payload").TryGetProperty("primaryTradeId",out _));
  var injected=await c.PostAsJsonAsync("/api/providers/signup",new{acceptTermsAndPrivacy=true,isVerified=true});Assert.Equal(HttpStatusCode.BadRequest,injected.StatusCode);
  injected=await c.PostAsJsonAsync("/api/providers/signup",new{acceptTermsAndPrivacy=true,primaryTradeId=1});Assert.Equal(HttpStatusCode.BadRequest,injected.StatusCode);
 }
 [Fact]
 public async Task Signup_authentication_failures_use_Response_and_do_not_change_existing_endpoints()
 {
  using var f=new Factory();using var anonymous=f.Client(false);var response=await anonymous.PostAsJsonAsync("/api/providers/signup",new{acceptTermsAndPrivacy=true});Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);using var body=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal(401,body.RootElement.GetProperty("statusCode").GetInt32());Assert.Equal("Cif",body.RootElement.GetProperty("messageLevel").GetString());Assert.Equal(JsonValueKind.Null,body.RootElement.GetProperty("payload").ValueKind);
  using var bad=f.Client(wrongSignature:true);Assert.Equal(HttpStatusCode.Unauthorized,(await bad.PostAsJsonAsync("/api/providers/signup",new{acceptTermsAndPrivacy=true})).StatusCode);
  using var c=f.Client();f.Session.Active=false;Assert.Equal(HttpStatusCode.Unauthorized,(await c.PostAsJsonAsync("/api/providers/signup",new{acceptTermsAndPrivacy=true})).StatusCode);
  f.Session.Unavailable=true;response=await c.PostAsJsonAsync("/api/providers/signup",new{acceptTermsAndPrivacy=true});Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);using var unavailable=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal(503,unavailable.RootElement.GetProperty("statusCode").GetInt32());
  Assert.Equal(0,f.Repository.Calls);
 }
 [Fact]
 public async Task Signup_binds_personal_data_to_the_authenticated_identity_and_fails_closed()
 {
  using var f=new Factory();using var c=f.Client();f.Identity.Mismatch=true;Assert.Equal(HttpStatusCode.ServiceUnavailable,(await c.PostAsJsonAsync("/api/providers/signup",new{acceptTermsAndPrivacy=true})).StatusCode);Assert.Equal(0,f.Repository.Calls);
  f.Identity.Mismatch=false;f.Identity.MissingPhone=true;Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/providers/signup",new{acceptTermsAndPrivacy=true})).StatusCode);Assert.Equal(0,f.Repository.Calls);
  f.Identity.MissingPhone=false;f.Repository.Fail=true;var response=await c.PostAsJsonAsync("/api/providers/signup",new{acceptTermsAndPrivacy=true});Assert.Equal(HttpStatusCode.InternalServerError,response.StatusCode);var text=await response.Content.ReadAsStringAsync();Assert.DoesNotContain("private connection",text);using var json=JsonDocument.Parse(text);Assert.Equal(500,json.RootElement.GetProperty("statusCode").GetInt32());
 }
 [Fact]
 public async Task Swagger_uses_shared_Response_for_signup_with_no_primary_trade_field()
 {
  using var f=new Factory();using var c=f.Client(false);using var json=JsonDocument.Parse(await c.GetStringAsync("/swagger/v1/swagger.json"));Assert.True(json.RootElement.GetProperty("paths").TryGetProperty("/api/providers/signup",out _));var properties=json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ProviderSignupRequest").GetProperty("properties");Assert.False(properties.TryGetProperty("primaryTradeId",out _));Assert.True(properties.TryGetProperty("acceptTermsAndPrivacy",out _));
 }
 private class Factory:WebApplicationFactory<Program>
 {
  const string Key="provider-signup-test-only-signing-key-123456789";public Guid User=Guid.NewGuid();public SessionFake Session=new();public SignupClientFake Identity=new();public SignupRepoFake Repository=new();
  protected override void ConfigureWebHost(IWebHostBuilder b)
  {
   b.UseEnvironment("Development");b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"Jwt:Key",Key},{"Jwt:Issuer","tests"},{"Jwt:Audience","clients"}}));
   b.ConfigureServices(s=>{s.RemoveAll<IProviderSignupRepository>();s.AddSingleton<IProviderSignupRepository>(Repository);s.RemoveAll<IProviderSignupClient>();s.AddSingleton<IProviderSignupClient>(Identity);s.RemoveAll<IIdentitySessionValidator>();s.AddSingleton<IIdentitySessionValidator>(Session);});
  }
  public HttpClient Client(bool authenticated=true,bool wrongSignature=false)
  {
   var c=CreateClient();if(authenticated){var token=new JwtSecurityToken("tests","clients",new[]{new Claim("sub",User.ToString()),new Claim("sid",Guid.NewGuid().ToString()),new Claim("application","App"),new Claim("role","Customer"),new Claim("providerId",Guid.NewGuid().ToString())},DateTime.UtcNow.AddSeconds(-1),DateTime.UtcNow.AddMinutes(5),new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key+(wrongSignature?"wrong":""))),SecurityAlgorithms.HmacSha256));c.DefaultRequestHeaders.Authorization=new("Bearer",new JwtSecurityTokenHandler().WriteToken(token));}return c;
  }
 }
 private class SessionFake:IIdentitySessionValidator
 {public bool Active=true,Unavailable;public Task<bool> IsActiveAsync(string token,bool https,CancellationToken ct){if(Unavailable)throw new HttpRequestException();return Task.FromResult(Active);}}
 private class SignupClientFake:IProviderSignupClient
 {public bool Mismatch,MissingPhone;public Task<SignupIdentity> IdentityAsync(Guid user,string b,CancellationToken ct)=>Task.FromResult(new SignupIdentity(Mismatch?Guid.NewGuid():user,"John","Smith",MissingPhone?null:"+447700900123"));}
 private class SignupRepoFake:IProviderSignupRepository
 {
  public int Calls;public bool Fail;
  public Task<ProviderSignupResult> CompleteAsync(Guid actor,string display,string? terms,string? privacy,CancellationToken ct)
  {Calls++;if(Fail)throw new InvalidOperationException("private connection failure");return Task.FromResult(new ProviderSignupResult(new(Guid.NewGuid(),actor,display,true,false,"Draft",1,null,[],[]),DateTime.UtcNow,terms,privacy));}
 }
}
