using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Application.Features.Administration;
using Identity.Application.Features.Signup;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
namespace Identity.Tests;
public class IndependentSurfaceTests
{
 private static object Input(string email="separate@example.test")=>new{firstName="Same",lastName="Person",email,phone="+447700900123",password="Separate-password-123",acceptTermsAndPrivacy=true};
 private static async Task<JsonElement> Payload(HttpResponseMessage r,HttpStatusCode status)
 {var body=await r.Content.ReadAsStringAsync();Assert.True(r.StatusCode==status,$"{r.StatusCode}: {body}");using var j=JsonDocument.Parse(body);return j.RootElement.GetProperty("payload").Clone();}
 [Fact]
 public async Task New_App_and_Tech_register_without_application_field_and_never_share_the_account()
 {
  using var f=new AuthFactory();using var c=f.NewClient();var app=await Payload(await c.PostAsJsonAsync("/api/app/register",new{firstName="Same",lastName="Person",email="same@example.test",phone="+447700900123",password="Separate-password-123"}),HttpStatusCode.Created);
  Assert.Equal("App",app.GetProperty("application").GetString());
  Assert.Equal(HttpStatusCode.Unauthorized,(await c.PostAsJsonAsync("/api/tech/login",new{userName="same@example.test",password="Separate-password-123"})).StatusCode);
  var tech=await Payload(await c.PostAsJsonAsync("/api/tech/register",Input("same@example.test")),HttpStatusCode.Created);
  Assert.Equal("Tech",tech.GetProperty("application").GetString());Assert.NotEqual(app.GetProperty("userId").GetGuid(),tech.GetProperty("userId").GetGuid());Assert.NotEqual(Guid.Empty,tech.GetProperty("providerId").GetGuid());Assert.Equal(1,f.Enrollment.Calls);
  Assert.Equal(HttpStatusCode.Conflict,(await c.PostAsJsonAsync("/api/tech/register",Input("SAME@example.test"))).StatusCode);
  await Payload(await c.PostAsJsonAsync("/api/app/login",new{userName="same@example.test",password="Separate-password-123"}),HttpStatusCode.OK);
  await Payload(await c.PostAsJsonAsync("/api/tech/login",new{userName="same@example.test",password="Separate-password-123"}),HttpStatusCode.OK);
 }
 [Fact]
 public async Task Tech_provider_outage_is_retryable_by_Tech_login_without_repeating_registration()
 {
  using var f=new AuthFactory();using var c=f.NewClient();f.Enrollment.Unavailable=true;
  Assert.Equal(HttpStatusCode.ServiceUnavailable,(await c.PostAsJsonAsync("/api/tech/register",Input())).StatusCode);Assert.Single(f.Users.GoogleUsers);Assert.Empty(f.Sessions.Items);
  f.Enrollment.Unavailable=false;await Payload(await c.PostAsJsonAsync("/api/tech/login",new{userName="separate@example.test",password="Separate-password-123"}),HttpStatusCode.OK);
  Assert.Equal(HttpStatusCode.Unauthorized,(await c.PostAsJsonAsync("/api/app/login",new{userName="separate@example.test",password="Separate-password-123"})).StatusCode);
 }
 [Fact]
 public async Task Tech_consent_is_required_and_route_scope_cannot_be_overridden_in_the_body()
 {
  using var f=new AuthFactory();using var c=f.NewClient();
  Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/tech/register",new{firstName="Same",lastName="Person",email="same@example.test",phone="+447700900123",password="Separate-password-123",acceptTermsAndPrivacy=false})).StatusCode);
  Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/app/login",new{userName="alice",password="Password-for-tests-1",application="Admin"})).StatusCode);Assert.Empty(f.Sessions.Items);
 }
 [Fact]
 public async Task Scoped_refresh_uses_its_own_cookie_and_rejects_a_token_from_another_table()
 {
  using var f=new AuthFactory();using var c=f.NewClient();var login=await c.PostAsJsonAsync("/api/app/login",new{userName="alice",password="Password-for-tests-1"});Assert.Equal(HttpStatusCode.OK,login.StatusCode);
  var cookie=login.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("zumaris_refresh_token_app=")).Split(';')[0].Split('=',2)[1];
  using var wrong=new HttpRequestMessage(HttpMethod.Post,"/api/tech/refresh");wrong.Headers.Add("Cookie","zumaris_refresh_token_tech="+cookie);Assert.Equal(HttpStatusCode.Forbidden,(await c.SendAsync(wrong)).StatusCode);
  using var right=new HttpRequestMessage(HttpMethod.Post,"/api/app/refresh");right.Headers.Add("Cookie","zumaris_refresh_token_app="+cookie);await Payload(await c.SendAsync(right),HttpStatusCode.OK);
 }
 [Fact]
 public async Task Admin_has_its_own_register_and_login_and_never_reuses_the_App_id()
 {
  using var f=new AuthFactory();var repo=new AdminRepository(f);using var host=f.WithWebHostBuilder(b=>
  {b.ConfigureAppConfiguration((_,cfg)=>cfg.AddInMemoryCollection(new Dictionary<string,string?>{{"AdminBootstrap:Enabled","true"},{"AdminBootstrap:Key","independent-admin-test-only-key-12345678901234567890"}}));b.ConfigureServices(s=>{s.RemoveAll<IAdministratorAccountRepository>();s.AddSingleton<IAdministratorAccountRepository>(repo);});});
  using var c=host.CreateClient(new(){BaseAddress=new("https://localhost"),HandleCookies=false});c.DefaultRequestHeaders.Add("X-Zumaris-CSRF","1");
  Assert.Equal(HttpStatusCode.Unauthorized,(await c.PostAsJsonAsync("/api/admin/register",new{firstName="Same",lastName="Person",email=f.Users.User.Email,phone="+447700900123",password="Password-for-tests-1"})).StatusCode);
  c.DefaultRequestHeaders.Add("X-Zumaris-Bootstrap-Key","independent-admin-test-only-key-12345678901234567890");
  var response=await c.PostAsJsonAsync("/api/admin/register",new{firstName="Same",lastName="Person",email=f.Users.User.Email,phone="+447700900123",password="Password-for-tests-1"});
  var admin=await Payload(response,HttpStatusCode.Created);Assert.NotEqual(f.Users.User.Id,admin.GetProperty("userId").GetGuid());Assert.False(response.Headers.Contains("Set-Cookie"));
  var token=await Payload(await c.PostAsJsonAsync("/api/admin/login",new{userName=f.Users.User.Email,password="Password-for-tests-1"}),HttpStatusCode.OK);Assert.Equal("Admin",token.GetProperty("role").GetString());
 }
 [Fact]
 public async Task Swagger_has_three_separate_register_login_refresh_and_logout_paths()
 {
  using var f=new AuthFactory();using var c=f.NewClient();using var j=JsonDocument.Parse(await c.GetStringAsync("/swagger/v1/swagger.json"));var paths=j.RootElement.GetProperty("paths");
  foreach(var app in new[]{"app","tech","admin"})foreach(var action in new[]{"register","login","refresh","logout"})Assert.True(paths.TryGetProperty($"/api/{app}/{action}",out _));
 }
 private sealed class AdminRepository(AuthFactory f):IAdministratorAccountRepository
 {
  private bool used;
  public async Task<AdministratorAccountResponse> CreateAsync(SignupAccount a,Guid? actor,DateTime now,CancellationToken ct)
  {if(actor is null&&used)throw new SignupException(409,"Bootstrap is already completed.");var user=await f.Accounts.CreateAsync("Admin",a,ct);f.ApplicationAdmins.Users.Add(user.Id);used=true;return new(user.Id,user.UserName,user.Email,a.FirstName,a.LastName,a.Phone,"Admin",now,actor);}
 }
}
