using Identity.Api.Security;
using Identity.Application.Features.Administration;
using Identity.Application.Features.Auth;
using Identity.Application.Features.Auth.Models;
using Identity.Application.Features.Signup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.BuildingBlocks.Common.Security;
namespace Identity.Api.Controllers;

[ApiController,AuthRequest,ResponseErrors,ServiceFilter(typeof(SignupExceptionFilter))]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None),RequestSizeLimit(16384)]
public abstract class IndependentAccountController(IAuthService auth,string application):ControllerBase
{
 protected static Response<T> Success<T>(T payload,int status,string message)=>new(payload){StatusCode=status,Title=status==201?"Created":"Success",Message=message,MessageLevel=MessageLevel.Cif};
 protected void Cookie(string token,DateTime expires)
 {
  Response.Cookies.Append(AuthCookies.NameFor(application),token,new(){HttpOnly=true,Secure=!HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(),SameSite=SameSiteMode.Strict,Path="/",IsEssential=true,Expires=new DateTimeOffset(DateTime.SpecifyKind(expires,DateTimeKind.Utc))});
 }
 private void DeleteCookie()=>Response.Cookies.Delete(AuthCookies.NameFor(application),new(){HttpOnly=true,Secure=!HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(),SameSite=SameSiteMode.Strict,Path="/",IsEssential=true});
 [HttpPost("login"),AllowAnonymous]
 [ProducesResponseType(typeof(Response<AuthResponse>),200)]
 public async Task<IActionResult> Login(AccountLoginRequest input,CancellationToken ct)
 {
  try{var(result,refresh)=await auth.LoginAsync(new LoginRequest(input.Email,input.Password,application),ct);Cookie(refresh,result.RefreshTokenExpiresAtUtc);return Ok(Success(result,200,"Signed in successfully."));}
  catch(UnauthorizedAccessException){return StatusCode(401,ApiResponseError.Create(401,"Invalid email or password.",HttpContext.TraceIdentifier));}
  catch(ApplicationLoginException ex){return StatusCode(ex.Status,ApiResponseError.Create(ex.Status,ex.Message,HttpContext.TraceIdentifier));}
 }
 [HttpPost("refresh"),AllowAnonymous]
 [ProducesResponseType(typeof(Response<AuthResponse>),200)]
 public async Task<IActionResult> Refresh(CancellationToken ct)
 {
  if(User.Identity?.IsAuthenticated==true&&User.FindFirst("application")?.Value!=application)return StatusCode(403,ApiResponseError.Create(403,"This token belongs to another application.",HttpContext.TraceIdentifier));
  try{var(result,refresh)=await auth.RefreshAsync(Request.Cookies[AuthCookies.NameFor(application)]??"",ct,application);Cookie(refresh,result.RefreshTokenExpiresAtUtc);return Ok(Success(result,200,"Session refreshed successfully."));}
  catch(UnauthorizedAccessException){DeleteCookie();return StatusCode(401,ApiResponseError.Create(401,"The session is invalid or expired. Sign in again.",HttpContext.TraceIdentifier));}
  catch(ApplicationLoginException ex){if(ex.Status==403)DeleteCookie();return StatusCode(ex.Status,ApiResponseError.Create(ex.Status,ex.Message,HttpContext.TraceIdentifier));}
 }
 [HttpPost("logout"),AllowAnonymous]
 [ProducesResponseType(204)]
 public async Task<IActionResult> Logout(CancellationToken ct)
 {
  if(User.Identity?.IsAuthenticated==true&&User.FindFirst("application")?.Value!=application)return StatusCode(403,ApiResponseError.Create(403,"This token belongs to another application.",HttpContext.TraceIdentifier));
  try{await auth.LogoutAsync(Request.Cookies[AuthCookies.NameFor(application)]??"",ct,application);DeleteCookie();return NoContent();}
  catch(ApplicationLoginException ex){return StatusCode(ex.Status,ApiResponseError.Create(ex.Status,ex.Message,HttpContext.TraceIdentifier));}
 }
}
[Route("api/app")]
public sealed class AppAccountsController(IAuthService auth,SignupService signup):IndependentAccountController(auth,"App")
{
 [HttpPost("register"),AllowAnonymous]
 [ProducesResponseType(typeof(Response<AuthResponse>),201)]
 public async Task<IActionResult> Register(SignupRequest input,CancellationToken ct)
 {var(result,refresh)=await signup.CreateAsync(input,ct);Cookie(refresh,result.RefreshTokenExpiresAtUtc);return StatusCode(201,Success(result,201,"App account created successfully."));}
}
[Route("api/tech")]
public sealed class TechAccountsController(IAuthService auth,IndependentRegistrationService signup):IndependentAccountController(auth,"Tech")
{
 [HttpPost("register"),AllowAnonymous]
 [ProducesResponseType(typeof(Response<AuthResponse>),201)]
 public async Task<IActionResult> Register(TechnicianRegisterRequest input,CancellationToken ct)
 {var(result,refresh)=await signup.RegisterTechAsync(input,ct);Cookie(refresh,result.RefreshTokenExpiresAtUtc);return StatusCode(201,Success(result,201,"Tech account created successfully."));}
}
public sealed class AdministratorRegistrationFilter(ActiveAdministratorFilter admins,AdministratorBootstrapKeyFilter bootstrap):IAsyncAuthorizationFilter
{
 public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
 {
  if(context.HttpContext.User.Identity?.IsAuthenticated==true)
  {
   await admins.OnAuthorizationAsync(context);
   if(context.Result is null)context.HttpContext.Items["AdministratorCreator"]=Guid.Parse(context.HttpContext.User.FindFirst("sub")!.Value);
  }
  else bootstrap.OnAuthorization(context);
 }
}
[Route("api/admin")]
public sealed class AdminAccountsController(IAuthService auth,AdministratorAccountService accounts):IndependentAccountController(auth,"Admin")
{
 [HttpPost("register"),AllowAnonymous,ServiceFilter(typeof(AdministratorRegistrationFilter))]
 [ProducesResponseType(typeof(Response<AdministratorAccountResponse>),201)]
 public async Task<IActionResult> Register(SignupRequest input,CancellationToken ct)
 {
  var actor=HttpContext.Items["AdministratorCreator"]as Guid?;
  var result=await accounts.CreateAsync(input,actor,ct);return StatusCode(201,Success(result,201,"Admin account created successfully. Sign in to Admin to continue."));
 }
}
