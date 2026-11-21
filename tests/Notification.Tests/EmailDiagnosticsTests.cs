using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using Zumaris.Notification.Application;
using Zumaris.Notification.Api.Controllers;
namespace Notification.Tests;
public class EmailDiagnosticsTests
{
 [Fact] public async Task Real_smtp_adapter_sends_fixed_diagnostic_without_recovery_token()
 {
  var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();
  try
  {
   var delivery=Task.Run(async()=>{
    using var peer=await listener.AcceptTcpClientAsync();await using var stream=peer.GetStream();using var reader=new StreamReader(stream);await using var writer=new StreamWriter(stream){AutoFlush=true,NewLine="\r\n"};
    await writer.WriteLineAsync("220 test SMTP");var content=new StringBuilder();
    while(await reader.ReadLineAsync() is { } line)
    {
     if(line.StartsWith("DATA")){await writer.WriteLineAsync("354 Send message");while(await reader.ReadLineAsync() is { } row && row!=".")content.AppendLine(row);await writer.WriteLineAsync("250 Accepted");return content.ToString();}
     await writer.WriteLineAsync("250 OK");
    }
    return content.ToString();
   });
   var sender=new Zumaris.Notification.Infrastructure.RecoverySmtpSender(new SmtpSettings{Host="127.0.0.1",Port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port,FromAddress="sender@example.test",EnableTls=false,DevelopmentLoopback=true});
   await sender.SendTest("recipient@example.test",default);
   var message=await delivery.WaitAsync(TimeSpan.FromSeconds(10));Assert.Contains("Subject: Zumaris SMTP test",message);Assert.Contains("recipient@example.test",message);Assert.DoesNotContain("#token=",message);
  }
  finally{listener.Stop();}
 }
 [Fact] public async Task Identity_outage_fails_closed_without_smtp()
 {using var f=new Factory();f.Access.Unavailable=true;using var c=f.Client();Assert.Equal(HttpStatusCode.ServiceUnavailable,(await c.PostAsJsonAsync("/api/admin/email/test",new{recipient="recipient@example.test"})).StatusCode);Assert.Equal(0,f.Sender.Calls);}

 [Fact] public async Task Admin_gets_actual_smtp_acceptance_and_fixed_test_template()
 {
  using var f=new Factory();using var c=f.Client();var response=await c.PostAsJsonAsync("/api/admin/email/test",new{recipient="recipient@example.test"});
  Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains("acceptedBySmtp",await response.Content.ReadAsStringAsync());Assert.Equal(1,f.Sender.Calls);
 }
 [Theory][InlineData("App")][InlineData("Tech")][InlineData(null)]
 public async Task Only_admin_can_send(string? side)
 {using var f=new Factory();using var c=f.Client(side);var response=await c.PostAsJsonAsync("/api/admin/email/test",new{recipient="recipient@example.test"});Assert.Equal(side is null?HttpStatusCode.Unauthorized:HttpStatusCode.Forbidden,response.StatusCode);Assert.Equal(0,f.Sender.Calls);}
 [Fact] public async Task Disabled_and_inactive_admin_never_send()
 {
  using var f=new Factory(false);using var c=f.Client();Assert.Equal(HttpStatusCode.NotFound,(await c.PostAsJsonAsync("/api/admin/email/test",new{recipient="recipient@example.test"})).StatusCode);Assert.Equal(0,f.Sender.Calls);
  using var active=new Factory();active.Access.Active=false;using var a=active.Client();Assert.Equal(HttpStatusCode.Forbidden,(await a.PostAsJsonAsync("/api/admin/email/test",new{recipient="recipient@example.test"})).StatusCode);Assert.Equal(0,active.Sender.Calls);
 }
 [Fact] public async Task Smtp_errors_are_safe_and_validation_rejects_arbitrary_content()
 {
  using var f=new Factory();using var c=f.Client();Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/admin/email/test",new{recipient="bad"})).StatusCode);
  Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/admin/email/test",new{recipient="recipient@example.test",body="arbitrary"})).StatusCode);
  f.Sender.Fail=true;var response=await c.PostAsJsonAsync("/api/admin/email/test",new{recipient="recipient@example.test"});Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);var body=await response.Content.ReadAsStringAsync();Assert.Contains("failureCode",body);Assert.DoesNotContain("secret-password",body);
 }
 [Fact] public async Task Limit_blocks_fourth_send()
 {using var f=new Factory();using var c=f.Client();for(int i=0;i<3;i++)Assert.Equal(HttpStatusCode.OK,(await c.PostAsJsonAsync("/api/admin/email/test",new{recipient="recipient@example.test"})).StatusCode);Assert.Equal(HttpStatusCode.TooManyRequests,(await c.PostAsJsonAsync("/api/admin/email/test",new{recipient="recipient@example.test"})).StatusCode);Assert.Equal(3,f.Sender.Calls);}
 [Fact] public async Task Admin_token_cannot_use_internal_password_recovery_sender()
 {using var f=new Factory();using var c=f.Client();Assert.Equal(HttpStatusCode.Unauthorized,(await c.PostAsJsonAsync("/api/internal/password-recovery-email",new{})).StatusCode);}
 private sealed class Factory(bool enabled=true):WebApplicationFactory<Program>
 {
  const string Key="notification-test-key-not-production-123456789";public Sender Sender=new();public Access Access=new();
  protected override void ConfigureWebHost(IWebHostBuilder b)
  {
   b.UseSetting("Jwt:Key",Key);b.UseSetting("Jwt:Issuer","tests");b.UseSetting("Jwt:Audience","clients");b.UseSetting("EmailDiagnostics:Enabled",enabled.ToString());b.UseEnvironment("Development");b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"Jwt:Key",Key},{"Jwt:Issuer","tests"},{"Jwt:Audience","clients"},{"EmailDiagnostics:Enabled",enabled.ToString()}}));
   b.ConfigureServices(s=>{s.RemoveAll<IEmailDiagnosticsSender>();s.AddSingleton<IEmailDiagnosticsSender>(Sender);s.RemoveAll<IAdminAccessVerifier>();s.AddSingleton<IAdminAccessVerifier>(Access);});
  }
  public HttpClient Client(string? side="Admin")
  {
   var c=CreateClient();if(side is not null){var token=new JwtSecurityToken("tests","clients",new[]{new Claim("sub","test-admin"),new Claim("sid",Guid.NewGuid().ToString()),new Claim("application",side),new Claim("role",side=="Admin"?"Admin":"Customer")},DateTime.UtcNow.AddSeconds(-1),DateTime.UtcNow.AddMinutes(5),new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),SecurityAlgorithms.HmacSha256));c.DefaultRequestHeaders.Authorization=new("Bearer",new JwtSecurityTokenHandler().WriteToken(token));}return c;
  }
 }
 private sealed class Sender:IEmailDiagnosticsSender {public int Calls;public bool Fail;public Task SendTest(string recipient,CancellationToken ct){Calls++;if(Fail)throw new SmtpException("secret-password should never be exposed");return Task.CompletedTask;}}
 private sealed class Access:IAdminAccessVerifier {public bool Active=true;public bool Unavailable;public Task<bool> Verify(string token,bool https,CancellationToken ct)=>Unavailable?throw new HttpRequestException():Task.FromResult(Active);}
}
