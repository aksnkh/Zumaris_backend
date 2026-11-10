using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Application.Abstractions.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
namespace Identity.Tests;
public class AdminAccessTests
{
 [Fact]
 public async Task Admin_access_requires_current_session_and_server_granted_membership()
 {
  using var source=new AuthFactory();var admins=new Admins();
  using var f=source.WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPlatformAdminRepository>();s.AddSingleton<IPlatformAdminRepository>(admins);}));
  using var c=f.CreateClient(new(){BaseAddress=new Uri("https://localhost"),HandleCookies=false});c.DefaultRequestHeaders.Add("X-Zumaris-CSRF","1");
  Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/auth/session/admin-access")).StatusCode);
  var login=await c.PostAsJsonAsync("/api/app/login", new {email="alice@example.test",password="Password-for-tests-1"});Assert.Equal(HttpStatusCode.OK,login.StatusCode);
  using var json=JsonDocument.Parse(await login.Content.ReadAsStringAsync());c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",json.RootElement.GetProperty("payload").GetProperty("accessToken").GetString());
  Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/auth/session/admin-access")).StatusCode);
  source.Accounts.Seed("Admin");
  admins.Allowed=true;
  var adminLogin=await c.PostAsJsonAsync("/api/admin/login", new {email="alice@example.test",password="Password-for-tests-1"});Assert.Equal(HttpStatusCode.OK,adminLogin.StatusCode);
  using var adminJson=JsonDocument.Parse(await adminLogin.Content.ReadAsStringAsync());c.DefaultRequestHeaders.Authorization=new("Bearer",adminJson.RootElement.GetProperty("payload").GetProperty("accessToken").GetString());
  Assert.Equal(HttpStatusCode.NoContent,(await c.GetAsync("/api/auth/session/admin-access")).StatusCode);
  admins.Allowed=false;Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/auth/session/admin-access")).StatusCode);
 }
 private class Admins:IPlatformAdminRepository
 {public bool Allowed;public Task<bool> IsAdminAsync(Guid user,CancellationToken ct)=>Task.FromResult(Allowed);}
}
