using Zumaris.BuildingBlocks.Common.Security;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zumaris.Provider.Application;
namespace Zumaris.Provider.Api.Controllers;
public sealed class ProviderWorkflowFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if(context.Exception is not ProviderWorkflowException ex)return;
        context.Result=new ObjectResult(new {message=ex.Message,statusCode=ex.Status}){StatusCode=ex.Status};context.ExceptionHandled=true;
    }
}
public sealed class ProviderAdminFilter(IProviderWorkflowClient workflow) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context,ActionExecutionDelegate next)
    {
        var bearer=context.HttpContext.Request.Headers.Authorization.ToString()["Bearer ".Length..];
        if(!await workflow.IsAdminAsync(bearer,context.HttpContext.RequestAborted))
        {context.Result=new ObjectResult(new {message="Administrator access is required."}){StatusCode=403};return;}
        await next();
    }
}
public sealed class DocumentForm
{
    [Required,StringLength(80,MinimumLength=1)] public string Kind {get;set;}="";
    [Required] public IFormFile File {get;set;}=null!;
}
[ApiController,Authorize,ServiceFilter(typeof(ProviderWorkflowFilter))]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/providers")]
public sealed class ProviderAccountController(IProviderAccountRepository repository,IProviderWorkflowClient workflow):ControllerBase
{
    private Guid UserId=>Guid.Parse(User.FindFirst("sub")!.Value);
    private string Bearer=>Request.Headers.Authorization.ToString()["Bearer ".Length..];
    [HttpGet("me")]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {var item=await repository.GetAsync(UserId,ct);return item is null?NotFound(new{message="Provider profile not found."}):Ok(item);}
    [HttpPost("me"),HttpPut("me")]
    public async Task<IActionResult> Save(SaveProviderRequest input,CancellationToken ct)=>Ok(await repository.SaveAsync(UserId,input,ct));
    [HttpGet("me/eligibility")]
    public async Task<IActionResult> Eligibility(CancellationToken ct)
    {
        var p=await repository.GetAsync(UserId,ct);
        if(p is null||!p.IsActive||!p.IsVerified||p.OnboardingStatus!="Approved")return StatusCode(403,new{message="A verified, active provider profile with reviewed documents is required."});
        return Ok(new ProviderEligibility(p.ProviderId,p.UserId,p.CategoryIds));
    }
    [HttpPost("me/documents"),RequestSizeLimit(6*1024*1024)]
    public async Task<IActionResult> Upload([FromForm]DocumentForm input,CancellationToken ct)
    {
        const int max=5*1024*1024;
        if(input.File.Length is <1 or >max||string.IsNullOrWhiteSpace(input.Kind))throw new ProviderWorkflowException(400,"Provide a document type and a PDF, JPEG or PNG document up to 5 MiB.");
        using var data=new MemoryStream();var buffer=new byte[81920];int read;
        await using(var stream=input.File.OpenReadStream())while((read=await stream.ReadAsync(buffer,ct))>0)
        {if(data.Length+read>max)throw new ProviderWorkflowException(400,"Document exceeds 5 MiB.");await data.WriteAsync(buffer.AsMemory(0,read),ct);}
        var bytes=data.ToArray();
        string type=bytes.AsSpan().StartsWith("%PDF-"u8)?"application/pdf":bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10})?"image/png":bytes.Length>=3&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255?"image/jpeg":"";
        if(type.Length==0||bytes.Length!=input.File.Length)throw new ProviderWorkflowException(400,"Only PDF, JPEG and PNG documents are accepted.");
        var name=Path.GetFileName(input.File.FileName.Replace('\\','/'));if(string.IsNullOrWhiteSpace(name)||name.Length>200||name.Any(char.IsControl))throw new ProviderWorkflowException(400,"The document filename is invalid.");
        return Ok(await repository.AddDocumentAsync(UserId,new(input.Kind.Trim(),name,type,bytes),ct));
    }
    [HttpGet("me/documents/{documentId:guid}")]
    public async Task<IActionResult> Document(Guid documentId,CancellationToken ct)
    {
        var p=await repository.GetAsync(UserId,ct)??throw new ProviderWorkflowException(404,"Provider profile not found.");
        var doc=await repository.DocumentAsync(UserId,p.ProviderId,documentId,ct)??throw new ProviderWorkflowException(404,"Document not found.");
        Response.Headers.XContentTypeOptions="nosniff";return File(doc.Content,"application/octet-stream",doc.FileName);
    }
    [HttpPost("/api/service-requests/{requestId:guid}/review")]
    [ApplicationAccess("App")]
    public async Task<IActionResult> Review(Guid requestId,CustomerReviewRequest input,CancellationToken ct)
    {
        var work=await workflow.CompletedWorkAsync(requestId,Bearer,ct);
        return Ok(new {message="Review submitted successfully.",payload=await repository.AddReviewAsync(UserId,work,input,ct)});
    }
    [HttpGet("{providerId:guid}/reviews")]
    [ApplicationAccess("App", "Tech", "Admin")]
    public async Task<IActionResult> Reviews(Guid providerId,[FromQuery,Range(1,int.MaxValue)]int pageNumber=1,[FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)=>Ok(await repository.ReviewsAsync(providerId,pageNumber,pageSize,ct));
}
[ApiController,Authorize,ServiceFilter(typeof(ProviderWorkflowFilter)),ServiceFilter(typeof(ProviderAdminFilter))]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/admin/providers")]
[ApplicationAccess("Admin")]
public sealed class ProviderAdministrationController(IProviderAccountRepository repository):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Pending([FromQuery,Range(1,int.MaxValue)]int pageNumber=1,[FromQuery,Range(1,100)]int pageSize=20,CancellationToken ct=default)=>Ok(await repository.PendingAsync(pageNumber,pageSize,ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id,CancellationToken ct)
    {var item=await repository.ByIdAsync(id,ct);return item is null?NotFound(new{message="Provider profile not found."}):Ok(item);}
    [HttpGet("{id:guid}/documents/{documentId:guid}")]
    public async Task<IActionResult> Document(Guid id,Guid documentId,CancellationToken ct)
    {
        var doc=await repository.DocumentAsync(null,id,documentId,ct)??throw new ProviderWorkflowException(404,"Document not found.");
        Response.Headers.XContentTypeOptions="nosniff";return File(doc.Content,"application/octet-stream",doc.FileName);
    }
    [HttpPost("{id:guid}/verification")]
    public async Task<IActionResult> Verify(Guid id,ReviewProviderRequest input,CancellationToken ct)=>Ok(await repository.ReviewAsync(Guid.Parse(User.FindFirst("sub")!.Value),id,input,ct));
}
