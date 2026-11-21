using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.Notification.Application;
namespace Zumaris.Notification.Api.Controllers;
public interface IAdminAccessVerifier { Task<bool> Verify(string token,bool https,CancellationToken ct); }
public sealed class AdminAccessVerifier(HttpClient client):IAdminAccessVerifier
{
 public async Task<bool> Verify(string token,bool https,CancellationToken ct)
 {
  using var request=new HttpRequestMessage(HttpMethod.Get,"api/auth/session/admin-access");
  request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
  if(https)request.Headers.Add("X-Forwarded-Proto","https");
  using var response=await client.SendAsync(request,ct);
  if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)return false;
  if(response.StatusCode!=HttpStatusCode.NoContent)throw new HttpRequestException("Administrator verification unavailable.");
  return true;
 }
}
public sealed record TestEmailRequest([Required,EmailAddress,StringLength(254)]string Recipient);
[ApiController,Route("api/admin/email/test")]
[Authorize(AuthenticationSchemes="AdminDiagnostics",Policy="EmailDiagnosticsAdmin")]
[EnableRateLimiting("EmailDiagnostics"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None),RequestSizeLimit(4096)]
public sealed class EmailDiagnosticsController(IEmailDiagnosticsSender sender,IAdminAccessVerifier access,IConfiguration config,ILogger<EmailDiagnosticsController> logger):ControllerBase
{
 private IActionResult Error(int code,string message)=>StatusCode(code,ApiResponseError.Create(code,message,HttpContext.TraceIdentifier));
 [HttpPost]
 public async Task<IActionResult> Send(TestEmailRequest input,CancellationToken ct)
 {
  if(!config.GetValue<bool>("EmailDiagnostics:Enabled"))return Error(404,"Email diagnostics are disabled.");
  if(!MailAddress.TryCreate(input.Recipient,out var recipient)||recipient.Address!=input.Recipient)return Error(400,"Enter a valid recipient email address.");
  try { if(!await access.Verify(Request.Headers.Authorization.ToString()["Bearer ".Length..],Request.IsHttps,ct))return Error(403,"Active administrator access is required."); }
  catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException){return Error(503,"Administrator verification is temporarily unavailable.");}
  try
  {
   await sender.SendTest(input.Recipient,ct);
   return Ok(new Response<object>(new{acceptedBySmtp=true}){StatusCode=200,Title="Success",Message="The SMTP server accepted the test email. Inbox delivery is not guaranteed.",MessageLevel=MessageLevel.Cif});
  }
  catch(Exception ex) when(ex is SmtpException or InvalidOperationException or OperationCanceledException or AuthenticationException or SocketException)
  {
   var code=ex switch {InvalidOperationException=>"Configuration",AuthenticationException=>"Tls",OperationCanceledException=>"Timeout",SocketException=>"Connection",SmtpException smtp when smtp.StatusCode==SmtpStatusCode.ClientNotPermitted=>"AuthenticationOrTls",SmtpException=>"SmtpRejected",_=>"Delivery"};
   logger.LogWarning("SMTP diagnostic failed ({FailureCode}, {FailureType}).",code,ex.GetType().Name);
   return StatusCode(503,new Response<object>(new{failureCode=code,smtpStatusCode=ex is SmtpException e?(int?)e.StatusCode:null}){StatusCode=503,Title="Service Error",Message="SMTP test failed. Check SMTP credentials, STARTTLS, sender permissions and outbound connectivity.",MessageLevel=MessageLevel.Cif});
  }
 }
}
