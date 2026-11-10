using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace Identity.Tests;
public class ApplicationLoginTests
{
 private static Task<HttpResponseMessage> Login(HttpClient c,string app)=>c.PostAsJsonAsync("/api/auth/login",new{userName="alice",password="Password-for-tests-1",application=app});
 private static async Task<(string Access,string Refresh)> Read(HttpResponseMessage r,string app)
 {
  Assert.Equal(HttpStatusCode.OK,r.StatusCode);using var j=JsonDocument.Parse(await r.Content.ReadAsStringAsync());var p=j.RootElement.GetProperty("payload");Assert.Equal(app,p.GetProperty("application").GetString());
  var cookie=r.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("zumaris_refresh_token_"+app.ToLowerInvariant()+"="));return(p.GetProperty("accessToken").GetString()!,cookie.Split(';')[0].Split('=',2)[1]);
 }
 private static Task<HttpResponseMessage> Refresh(HttpClient c,string app,string token){var r=new HttpRequestMessage(HttpMethod.Post,"/api/auth/refresh?application="+app);r.Headers.Add("Cookie","zumaris_refresh_token_"+app.ToLowerInvariant()+"="+token);return c.SendAsync(r);}
 [Theory][InlineData(null)][InlineData("")][InlineData("Unknown")][InlineData("tech")]
 public async Task Legacy_login_still_requires_explicit_application(string? app)
 {using var f=new AuthFactory();using var c=f.NewClient();Assert.Equal(HttpStatusCode.BadRequest,(await Login(c,app!)).StatusCode);Assert.Empty(f.Sessions.Items);}
 [Fact]
 public async Task App_credentials_do_not_authenticate_against_Tech_or_Admin_tables()
 {using var f=new AuthFactory();using var c=f.NewClient();foreach(var app in new[]{"Tech","Admin"})Assert.Equal(HttpStatusCode.Unauthorized,(await Login(c,app)).StatusCode);Assert.Empty(f.Sessions.Items);}
 [Fact]
 public async Task Same_email_and_username_in_three_sides_have_different_accounts_and_contexts()
 {
  using var f=new AuthFactory();using var c=f.NewClient();var tech=f.Accounts.Seed("Tech");var admin=f.Accounts.Seed("Admin");f.ApplicationAdmins.Users.Add(admin.Id);
  var appLogin=await Read(await Login(c,"App"),"App");var techLogin=await Read(await Login(c,"Tech"),"Tech");var adminLogin=await Read(await Login(c,"Admin"),"Admin");
  var subjects=new[]{appLogin,techLogin,adminLogin}.Select(x=>new JwtSecurityTokenHandler().ReadJwtToken(x.Access).Subject).ToArray();Assert.Equal(3,subjects.Distinct().Count());Assert.Contains(tech.Id.ToString(),subjects);Assert.Contains(admin.Id.ToString(),subjects);
  Assert.Equal(1,f.Enrollment.Calls);Assert.NotNull(new JwtSecurityTokenHandler().ReadJwtToken(techLogin.Access).Claims.Single(x=>x.Type=="providerId").Value);
 }
 [Fact]
 public async Task Refresh_does_not_switch_application_or_account_and_rechecks_active_account()
 {
  using var f=new AuthFactory();using var c=f.NewClient();var tech=f.Accounts.Seed("Tech");var app=await Read(await Login(c,"App"),"App");var t=await Read(await Login(c,"Tech"),"Tech");
  Assert.Equal(HttpStatusCode.Forbidden,(await Refresh(c,"Tech",app.Refresh)).StatusCode);
  var refreshed=await Read(await Refresh(c,"Tech",t.Refresh),"Tech");Assert.Equal(tech.Id.ToString(),new JwtSecurityTokenHandler().ReadJwtToken(refreshed.Access).Subject);
  tech.IsActive=false;Assert.Equal(HttpStatusCode.Unauthorized,(await Refresh(c,"Tech",refreshed.Refresh)).StatusCode);await Read(await Refresh(c,"App",app.Refresh),"App");
 }
 [Theory][InlineData(false,true)][InlineData(true,false)]
 public async Task Inactive_provider_is_denied_and_missing_consent_is_completed_from_tech_registration(bool active,bool consent)
 {
  using var f=new AuthFactory();using var c=f.NewClient();var tech=f.Accounts.Seed("Tech");f.ApplicationProviders.Items[tech.Id]=new(tech.Id,Guid.NewGuid(),active,consent);
  if(!consent)await Read(await Login(c,"Tech"),"Tech");else Assert.Equal(HttpStatusCode.Forbidden,(await Login(c,"Tech")).StatusCode);
 }
 [Fact]
 public async Task Missing_provider_service_does_not_issue_a_tech_session_or_break_app_login()
 {
  using var f=new AuthFactory();using var c=f.NewClient();f.Accounts.Seed("Tech");f.ApplicationProviders.Unavailable=true;
  Assert.Equal(HttpStatusCode.ServiceUnavailable,(await Login(c,"Tech")).StatusCode);Assert.Empty(f.Sessions.Items);await Read(await Login(c,"App"),"App");
 }
 [Fact]
 public async Task Admin_table_account_and_server_active_check_are_both_required()
 {
  using var f=new AuthFactory();using var c=f.NewClient();var admin=f.Accounts.Seed("Admin");
  Assert.Equal(HttpStatusCode.Forbidden,(await Login(c,"Admin")).StatusCode);f.ApplicationAdmins.Users.Add(admin.Id);var login=await Read(await Login(c,"Admin"),"Admin");f.ApplicationAdmins.Users.Clear();
  Assert.Equal(HttpStatusCode.Forbidden,(await Refresh(c,"Admin",login.Refresh)).StatusCode);
 }
}
