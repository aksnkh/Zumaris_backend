using Zumaris.BuildingBlocks.Common.Security;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zumaris.ServiceRequests.Application;
namespace Zumaris.ServiceRequests.Api.Controllers;
public sealed class CompleteWorkForm
{
 [Required,StringLength(10000,MinimumLength=1)]public string Report {get;set;}="";
 [Required,MinLength(1),MaxLength(8)]public List<IFormFile> Photos {get;set;}=[];
}
[ApiController,Authorize,ServiceFilter(typeof(ManagementExceptionFilter)),ServiceFilter(typeof(QuoteExceptionFilter))]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/technician")]
[ApplicationAccess("Tech")]
public sealed class TechnicianWorkController(TechnicianWorkService service,ITechnicianWorkRepository repository):ControllerBase
{
 private Guid Actor=>Guid.Parse(User.FindFirst("sub")!.Value);
 private string Bearer=>Request.Headers.Authorization.ToString()["Bearer ".Length..];
 [HttpGet("opportunities")]
 public async Task<IActionResult> Opportunities([FromQuery]int pageNumber=1,[FromQuery]int pageSize=20,CancellationToken ct=default)=>Ok(await service.OpportunitiesAsync(Actor,Bearer,pageNumber,pageSize,ct));
 [HttpGet("jobs")]
 public async Task<IActionResult> Jobs([FromQuery]string? status=null,[FromQuery]int pageNumber=1,[FromQuery]int pageSize=20,CancellationToken ct=default)=>Ok(await service.JobsAsync(Actor,status,pageNumber,pageSize,ct));
 [HttpGet("jobs/{id:guid}")]
 public async Task<IActionResult> Job(Guid id,CancellationToken ct)=>Ok(await service.JobAsync(Actor,id,false,ct));
 [HttpPost("jobs/{id:guid}/start")]
 public async Task<IActionResult> Start(Guid id,CancellationToken ct)=>Ok(new{message="Job started successfully.",payload=await service.StartAsync(Actor,id,ct)});
 [HttpPost("jobs/{id:guid}/complete"),RequestSizeLimit(44*1024*1024)]
 [RequestFormLimits(MultipartBodyLengthLimit=42*1024*1024)]
 public async Task<IActionResult> Complete(Guid id,[FromForm]CompleteWorkForm input,CancellationToken ct)
 {
  var streams=new List<Stream>();
  try
  {
   var photos=input.Photos.Select(p=>{var stream=p.OpenReadStream();streams.Add(stream);return new PhotoUpload(stream,p.Length);}).ToList();
   return Ok(new{message="Job completed successfully.",payload=await service.CompleteAsync(Actor,id,input.Report,photos,ct)});
  }
  finally {foreach(var stream in streams)await stream.DisposeAsync();}
 }
 [HttpGet("jobs/{id:guid}/photos/{photoId:guid}")]
 [ApplicationAccess("App", "Tech")]
 public async Task<IActionResult> Photo(Guid id,Guid photoId,CancellationToken ct)
 {var p=await service.PhotoAsync(Actor,id,photoId,ct,User.FindFirst("application")!.Value=="App");return File(p.Content,p.ContentType,p.FileName);}
 [HttpGet("/api/service-requests/{id:guid}/execution")]
 [ApplicationAccess("App")]
 public async Task<IActionResult> Execution(Guid id,CancellationToken ct)=>Ok(await service.JobAsync(Actor,id,true,ct));
 [HttpGet("/api/service-requests/{id:guid}/completed-work")]
 [ApplicationAccess("App")]
 public async Task<IActionResult> Completed(Guid id,CancellationToken ct)=>Ok(await service.CompletedAsync(Actor,id,ct));
 [HttpGet("providers/{providerId:guid}/work-stats")]
 [ApplicationAccess("App", "Tech", "Admin")]
 public async Task<IActionResult> Stats(Guid providerId,CancellationToken ct)=>Ok(new{completedJobs=await repository.CompletedJobsAsync(providerId,ct)});
}
