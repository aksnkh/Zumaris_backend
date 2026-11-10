using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.BuildingBlocks.Common.Security;
using Zumaris.Provider.Application;
namespace Zumaris.Provider.Api.Controllers;
public sealed record ProviderEnrollmentRequest([Required,StringLength(100)]string FirstName,[Required,StringLength(100)]string LastName,
 [Required,StringLength(40)]string Phone,bool AcceptTermsAndPrivacy);
[ApiController,InternalApplicationAccess,Authorize(AuthenticationSchemes="IdentityLoginAccess",Policy="IdentityProviderEnrollment")]
[ResponseErrors,ServiceFilter(typeof(VerificationExceptionFilter)),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[RequestSizeLimit(4096),Route("api/internal/users/{userId:guid}/signup")]
public sealed class ProviderEnrollmentController(IProviderSignupRepository repository,IConfiguration configuration):ControllerBase
{
 [HttpPost]
 public async Task<IActionResult> Register(Guid userId,ProviderEnrollmentRequest input,CancellationToken ct)
 {
  if(User.FindFirst("sub")?.Value!=userId.ToString())return StatusCode(403,ApiResponseError.Create(403,"This credential cannot enroll another account.",HttpContext.TraceIdentifier));
  if(!input.AcceptTermsAndPrivacy)throw new ProviderWorkflowException(400,"Provider terms consent is required.");
  var name=ProviderSignupService.ValidateIdentity(userId,new(userId,input.FirstName,input.LastName,input.Phone));
  var result=await repository.CompleteAsync(userId,name,configuration["ProviderTerms:Version"],configuration["PrivacyPolicy:Version"],ct);
  return Ok(new Response<ProviderSignupResult>(result){StatusCode=200,Title="Success",Message="Tech provider registration completed successfully.",MessageLevel=MessageLevel.Cif});
 }
}
