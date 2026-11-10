using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Identity.Infrastructure.Security;
using Xunit;
namespace Identity.Tests;
public class SignupTests
{
 private static object Input()=>new{firstName=" John ",lastName="Smith",email=" JOHN@example.test ",phone="+44 7700 900123",password="Password-for-tests-2"};
 [Fact]
 public async Task Signup_creates_names_phone_and_hashed_password_and_login_accepts_email()
 {
  using var f=new AuthFactory();using var c=f.NewClient();var response=await c.PostAsJsonAsync("/api/app/register",Input());Assert.Equal(HttpStatusCode.Created,response.StatusCode);
  using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var body=json.RootElement;Assert.Equal(201,body.GetProperty("statusCode").GetInt32());Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
  Assert.Equal("Cif",body.GetProperty("messageLevel").GetString());
  var payload=body.GetProperty("payload");var id=payload.GetProperty("userId").GetGuid();var user=Assert.Single(f.Users.GoogleUsers);
  Assert.Equal("john@example.test",user.Email);Assert.Equal(user.Email,user.UserName);Assert.NotEqual("Password-for-tests-2",user.PasswordHash);Assert.True(new PasswordHasher().Verify("Password-for-tests-2",user.PasswordHash!));
  var profile=f.Profiles.Records[id];Assert.Equal("John",profile.FirstName);Assert.Equal("Smith",profile.LastName);Assert.Equal("+447700900123",profile.Phone);
  Assert.False(payload.TryGetProperty("refreshToken",out _));Assert.Contains(response.Headers.GetValues("Set-Cookie"),v=>v.Contains("httponly",StringComparison.OrdinalIgnoreCase));
  c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",payload.GetProperty("accessToken").GetString());
  Assert.Equal(HttpStatusCode.NoContent,(await c.GetAsync("/api/auth/session")).StatusCode);
  c.DefaultRequestHeaders.Authorization=null;
  var login=await c.PostAsJsonAsync("/api/app/login", new {email="JOHN@example.test",password="Password-for-tests-2"});Assert.Equal(HttpStatusCode.OK,login.StatusCode);
  Assert.Equal(HttpStatusCode.Unauthorized,(await c.PostAsJsonAsync("/api/app/login", new {email="john@example.test",password="Wrong-password"})).StatusCode);
  var duplicate=await c.PostAsJsonAsync("/api/app/register",Input());Assert.Equal(HttpStatusCode.Conflict,duplicate.StatusCode);using var conflict=JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());Assert.Equal(JsonValueKind.Null,conflict.RootElement.GetProperty("payload").ValueKind);
  Assert.Single(f.Users.GoogleUsers);
 }
 [Theory]
 [InlineData("firstName","")][InlineData("lastName"," ")][InlineData("email","invalid")][InlineData("phone","07700900123")][InlineData("password","short")]
 public async Task Invalid_signup_uses_the_shared_response_model_and_field_errors(string field,string value)
 {
  using var f=new AuthFactory();using var c=f.NewClient();var input=JsonSerializer.SerializeToNode(Input())!.AsObject();input[field]=value;
  var response=await c.PostAsJsonAsync("/api/app/register",input);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
  Assert.Equal(400,json.RootElement.GetProperty("statusCode").GetInt32());Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("message").GetString()));Assert.Equal(JsonValueKind.Null,json.RootElement.GetProperty("payload").ValueKind);Assert.Equal(JsonValueKind.Object,json.RootElement.GetProperty("errors").ValueKind);Assert.Empty(f.Users.GoogleUsers);
 }
 [Fact]
 public async Task Signup_rejects_csrf_missing_unknown_fields_and_never_exposes_database_errors()
 {
  using var f=new AuthFactory();using var noCsrf=f.NewClient(false);var response=await noCsrf.PostAsJsonAsync("/api/app/register",Input());Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);using var bad=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal(400,bad.RootElement.GetProperty("statusCode").GetInt32());Assert.Equal(JsonValueKind.Null,bad.RootElement.GetProperty("payload").ValueKind);
  using var c=f.NewClient();var input=JsonSerializer.SerializeToNode(Input())!.AsObject();input["isVerified"]=true;Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/app/register",input)).StatusCode);
  f.Signups.Fail=true;response=await c.PostAsJsonAsync("/api/app/register",Input());Assert.Equal(HttpStatusCode.InternalServerError,response.StatusCode);var content=await response.Content.ReadAsStringAsync();Assert.DoesNotContain("Private database",content);using var error=JsonDocument.Parse(content);Assert.Equal(500,error.RootElement.GetProperty("statusCode").GetInt32());
 }
 [Fact]
 public async Task Signup_is_documented_without_requiring_username_or_primary_trade()
 {
  using var f=new AuthFactory();using var c=f.NewClient();using var json=JsonDocument.Parse(await c.GetStringAsync("/swagger/v1/swagger.json"));var schemas=json.RootElement.GetProperty("components").GetProperty("schemas");var properties=schemas.GetProperty("SignupRequest").GetProperty("properties");Assert.False(properties.TryGetProperty("username",out _));Assert.False(properties.TryGetProperty("primaryTradeId",out _));Assert.True(json.RootElement.GetProperty("paths").TryGetProperty("/api/app/register",out _));
 }
}
