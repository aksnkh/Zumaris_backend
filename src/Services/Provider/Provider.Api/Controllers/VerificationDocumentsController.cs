using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.Provider.Application;
namespace Zumaris.Provider.Api.Controllers;

public sealed class VerificationExceptionFilter(ILogger<VerificationExceptionFilter> logger):IExceptionFilter
{
 public void OnException(ExceptionContext context)
 {
  if(context.Exception is OperationCanceledException&&context.HttpContext.RequestAborted.IsCancellationRequested)return;
  var ex=context.Exception as ProviderWorkflowException;var status=ex?.Status??500;
  if(ex is null)logger.LogError(context.Exception,"Verification document request failed.");
  context.Result=new ObjectResult(ApiResponseError.Create(status,ex?.Message??"Verification documents are temporarily unavailable.",context.HttpContext.TraceIdentifier)){StatusCode=status};context.ExceptionHandled=true;
 }
}
public sealed class VerificationAdminFilter(IProviderWorkflowClient workflow):IAsyncActionFilter
{
 public async Task OnActionExecutionAsync(ActionExecutingContext context,ActionExecutionDelegate next)
 {
  var bearer=context.HttpContext.Request.Headers.Authorization.ToString()["Bearer ".Length..];
  if(!await workflow.IsAdminAsync(bearer,context.HttpContext.RequestAborted))
  {context.Result=new ObjectResult(ApiResponseError.Create(403,"Administrator access is required.",context.HttpContext.TraceIdentifier)){StatusCode=403};return;}
  await next();
 }
}
public sealed class VerificationFileForm
{
 public IdentityDocumentKind? IdentityKind{get;set;}
 [Required]public IFormFile File{get;set;}=null!;
}
[ApiController,Authorize,ResponseErrors,ServiceFilter(typeof(VerificationExceptionFilter))]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/providers/me/verification-documents")]
public sealed class VerificationDocumentsController(VerificationDocumentService service,IProviderVerificationRepository repository):ControllerBase
{
 private Guid Actor=>Guid.Parse(User.FindFirst("sub")!.Value);
 private static Response<T> Success<T>(T data,string message)=>new(data){StatusCode=200,Title="Success",Message=message,MessageLevel=MessageLevel.Cif};
 [HttpGet]
 [ProducesResponseType(typeof(Response<VerificationOverview>),200)]
 public async Task<IActionResult> Overview(CancellationToken ct)=>Ok(Success(await repository.OverviewAsync(Actor,ct),"Verification documents retrieved successfully."));
 [HttpPut("profile")]
 [ProducesResponseType(typeof(Response<VerificationProfile>),200)]
 public async Task<IActionResult> SaveProfile(SaveVerificationProfile input,CancellationToken ct)=>Ok(Success(await repository.SaveProfileAsync(Actor,input,ct),"Verification profile saved successfully."));
 [HttpGet("{documentType}")]
 [ProducesResponseType(typeof(Response<VerificationDocumentDetails>),200)]
 public async Task<IActionResult> Details(VerificationDocumentType documentType,CancellationToken ct)=>Ok(Success(await repository.DetailsAsync(Actor,null,documentType,false,ct),"Document details retrieved successfully."));
 [HttpPost("{documentType}/files"),RequestSizeLimit(6*1024*1024)]
 [ProducesResponseType(typeof(Response<VerificationDocumentDetails>),200)]
 public async Task<IActionResult> Upload(VerificationDocumentType documentType,[FromForm]VerificationFileForm input,CancellationToken ct)
 {
  if(input.File.Length is <1 or >VerificationDocumentService.MaxFileBytes)throw new ProviderWorkflowException(400,"Each PDF, JPEG or PNG document must be 1 byte to 5 MiB.");
  await using var stream=input.File.OpenReadStream();using var data=new MemoryStream();var buffer=new byte[81920];int read;
  while((read=await stream.ReadAsync(buffer,ct))>0)
  {if(data.Length+read>VerificationDocumentService.MaxFileBytes)throw new ProviderWorkflowException(400,"Document exceeds 5 MiB.");await data.WriteAsync(buffer.AsMemory(0,read),ct);}
  if(data.Length!=input.File.Length)throw new ProviderWorkflowException(400,"Document length is invalid.");
  return Ok(Success(await service.UploadAsync(Actor,documentType,new(documentType.ToString(),input.File.FileName,input.File.ContentType,data.ToArray()),ct,input.IdentityKind),"Document upload processed successfully."));
 }
 [HttpPost("submit"),RequestSizeLimit(4096)]
 [ProducesResponseType(typeof(Response<VerificationOverview>),200)]
 public async Task<IActionResult> Submit(SubmitVerificationDocuments input,CancellationToken ct)=>Ok(Success(await service.SubmitAsync(Actor,input.DocumentIds,ct),"Documents submitted for administrator review."));
 [HttpPost("{documentType}/submit"),RequestSizeLimit(4096)]
 [ProducesResponseType(typeof(Response<VerificationDocumentDetails>),200)]
 public async Task<IActionResult> SubmitOne(VerificationDocumentType documentType,SubmitVerificationDocument input,CancellationToken ct)
 {
  var detail=await repository.DetailsAsync(Actor,null,documentType,false,ct);
  if(detail.Document.CurrentDocument?.DocumentId!=input.DocumentId)throw new ProviderWorkflowException(409,"The selected document changed. Reload before submitting.");
  await service.SubmitAsync(Actor,new[]{input.DocumentId},ct);
  return Ok(Success(await repository.DetailsAsync(Actor,null,documentType,false,ct),"Document submitted for administrator review."));
 }
}
[ApiController,Authorize,ResponseErrors,ServiceFilter(typeof(VerificationExceptionFilter)),ServiceFilter(typeof(VerificationAdminFilter))]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/admin")]
public sealed class VerificationDocumentAdministrationController(VerificationDocumentService service,IProviderVerificationRepository repository):ControllerBase
{
 private Guid Actor=>Guid.Parse(User.FindFirst("sub")!.Value);
 private static Response<T> Success<T>(T data,string message)=>new(data){StatusCode=200,Title="Success",Message=message,MessageLevel=MessageLevel.Cif};
 [HttpGet("verification-documents")]
 [ProducesResponseType(typeof(Response<PendingVerificationPage>),200)]
 public async Task<IActionResult> Pending([FromQuery,Range(1,int.MaxValue)]int pageNumber=1,[FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)=>Ok(Success(await repository.PendingAsync(pageNumber,pageSize,ct),"Pending verification documents retrieved successfully."));
 [HttpGet("providers/{providerId:guid}/verification-documents")]
 [ProducesResponseType(typeof(Response<VerificationOverview>),200)]
 public async Task<IActionResult> Overview(Guid providerId,CancellationToken ct)=>Ok(Success(await repository.AdminOverviewAsync(providerId,ct),"Verification documents retrieved successfully."));
 [HttpGet("providers/{providerId:guid}/verification-documents/history")]
 [ProducesResponseType(typeof(Response<IReadOnlyList<DocumentReviewAudit>>),200)]
 public async Task<IActionResult> History(Guid providerId,CancellationToken ct)=>Ok(Success(await repository.AuditAsync(providerId,ct),"Document review history retrieved successfully."));
 [HttpGet("providers/{providerId:guid}/verification-documents/{documentType}")]
 [ProducesResponseType(typeof(Response<VerificationDocumentDetails>),200)]
 public async Task<IActionResult> Details(Guid providerId,VerificationDocumentType documentType,CancellationToken ct)=>Ok(Success(await repository.DetailsAsync(null,providerId,documentType,true,ct),"Document details retrieved successfully."));
 [HttpPost("providers/{providerId:guid}/verification-documents/{documentType}/review"),RequestSizeLimit(4096)]
 [ProducesResponseType(typeof(Response<VerificationDocumentDetails>),200)]
 public async Task<IActionResult> Review(Guid providerId,VerificationDocumentType documentType,ReviewVerificationDocument input,CancellationToken ct)=>Ok(Success(await service.ReviewAsync(Actor,providerId,documentType,input,ct),input.Approved?"Document verified successfully.":"Document changes requested successfully."));
}
