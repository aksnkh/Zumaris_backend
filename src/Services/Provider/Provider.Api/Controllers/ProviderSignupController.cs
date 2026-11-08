using Zumaris.BuildingBlocks.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.Provider.Application;
namespace Zumaris.Provider.Api.Controllers;
public sealed class ProviderSignupExceptionFilter(ILogger<ProviderSignupExceptionFilter> logger):IExceptionFilter
{
 public void OnException(ExceptionContext context)
 {
  if(context.Exception is OperationCanceledException&&context.HttpContext.RequestAborted.IsCancellationRequested)return;
  var ex=context.Exception as ProviderWorkflowException;var status=ex?.Status??500;
  if(ex is null)logger.LogError(context.Exception,"Provider signup failed.");
  context.Result=new ObjectResult(ApiResponseError.Create(status,ex?.Message??"Provider registration is temporarily unavailable.",context.HttpContext.TraceIdentifier)){StatusCode=status};context.ExceptionHandled=true;
 }
}
[ApiController,Authorize,ResponseErrors,ServiceFilter(typeof(ProviderSignupExceptionFilter))]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[RequestSizeLimit(4096)]
[Route("api/providers/signup")]
[ApplicationAccess("App", "Tech")]
public sealed class ProviderSignupController(ProviderSignupService signup,IConfiguration configuration):ControllerBase
{
 [HttpPost]
 [ProducesResponseType(typeof(Response<ProviderSignupResult>),200)]
 [ProducesResponseType(typeof(ApiResponseError),400)]
 [ProducesResponseType(typeof(ApiResponseError),401)]
 [ProducesResponseType(typeof(ApiResponseError),503)]
 public async Task<IActionResult> Complete(ProviderSignupRequest input,CancellationToken ct)
 {
  var actor=Guid.Parse(User.FindFirst("sub")!.Value);var bearer=Request.Headers.Authorization.ToString()["Bearer ".Length..];
  var result=await signup.CompleteAsync(actor,input,bearer,configuration["ProviderTerms:Version"],configuration["PrivacyPolicy:Version"],ct);
  return Ok(new Response<ProviderSignupResult>(result){StatusCode=200,Title="Success",Message="Provider registration completed successfully. Submit your identity documents for administrator review.",MessageLevel=MessageLevel.Cif});
 }
}
