using Identity.Api.Security;
using Identity.Application.Features.Signup;
using Identity.Application.Features.Auth.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zumaris.BuildingBlocks.Common.Responses;
namespace Identity.Api.Controllers;
public sealed class SignupExceptionFilter(ILogger<SignupExceptionFilter> logger):IExceptionFilter
{
 public void OnException(ExceptionContext context)
 {
  if(context.Exception is OperationCanceledException&&context.HttpContext.RequestAborted.IsCancellationRequested)return;
  var ex=context.Exception as SignupException;var status=ex?.Status??500;
  if(ex is null)logger.LogError(context.Exception,"Account signup failed.");
  context.Result=new ObjectResult(ApiResponseError.Create(status,ex?.Message??"Account creation is temporarily unavailable.",context.HttpContext.TraceIdentifier,ex?.Errors)){StatusCode=status};context.ExceptionHandled=true;
 }
}
[ApiController,AllowAnonymous,AuthRequest,ResponseErrors,ServiceFilter(typeof(SignupExceptionFilter))]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[RequestSizeLimit(16384)]
[Route("api/auth/signup")]
public sealed class SignupController(SignupService signup):ControllerBase
{
 [HttpPost]
 [ProducesResponseType(typeof(Response<AuthResponse>),201)]
 [ProducesResponseType(typeof(ApiResponseError),400)]
 [ProducesResponseType(typeof(ApiResponseError),409)]
 [ProducesResponseType(typeof(ApiResponseError),503)]
 public async Task<IActionResult> Create(SignupRequest input,CancellationToken ct)
 {
  var (auth,refresh)=await signup.CreateAsync(input,ct);
  foreach(var legacyPath in new[]{"/api/auth","/identity/api/auth"})
   Response.Cookies.Delete(AuthController.CookieName,new CookieOptions{HttpOnly=true,Secure=!HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(),SameSite=SameSiteMode.Strict,Path=legacyPath,IsEssential=true});
  Response.Cookies.Append(AuthController.CookieName,refresh,new CookieOptions{HttpOnly=true,Secure=!HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(),SameSite=SameSiteMode.Strict,Path="/",IsEssential=true,Expires=new DateTimeOffset(DateTime.SpecifyKind(auth.RefreshTokenExpiresAtUtc,DateTimeKind.Utc))});
  return StatusCode(201,new Response<AuthResponse>(auth){StatusCode=201,Title="Created",Message="Account created successfully.",MessageLevel=MessageLevel.Cif});
 }
}
