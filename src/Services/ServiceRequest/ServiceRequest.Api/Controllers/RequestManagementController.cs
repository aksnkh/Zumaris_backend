using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zumaris.ServiceRequests.Application;
using Zumaris.ServiceRequests.Domain;

namespace Zumaris.ServiceRequests.Api.Controllers;

public sealed class ManagementExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            RequestNotFoundException => new NotFoundObjectResult(new ProblemDetails { Status = 404, Title = "Request or photo not found." }),
            RequestConflictException ex => new ConflictObjectResult(new ProblemDetails { Status = 409, Title = ex.Message }),
            RequestValidationException ex => new BadRequestObjectResult(new ValidationProblemDetails(ex.Errors) { Status = 400 }),
            _ => null
        };
        if (context.Result is not null) context.ExceptionHandled = true;
    }
}

public sealed class UploadPhotosForm
{
    public int Version { get; set; }
    public List<IFormFile> Photos { get; set; } = [];
}

[ApiController]
[Authorize]
[ServiceFilter(typeof(ManagementExceptionFilter))]
[Route("api/service-requests")]
public sealed class RequestManagementController(IRequestManagementService service) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] RequestStatus? status, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.ListAsync(UserId, status, page, pageSize, ct));
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.GetAsync(UserId, id, ct));
    }
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateServiceRequest request, CancellationToken ct) =>
        Ok(await service.UpdateAsync(UserId, id, request, ct));

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancelServiceRequest request, CancellationToken ct) =>
        Ok(await service.CancelAsync(UserId, id, request, ct));

    [HttpGet("{id:guid}/timeline")]
    public async Task<IActionResult> Timeline(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.TimelineAsync(UserId, id, ct));
    }
    [HttpPost("{id:guid}/photos")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(44 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 42 * 1024 * 1024)]
    public async Task<IActionResult> Photos(Guid id, [FromForm] UploadPhotosForm form, CancellationToken ct)
    {
        var streams = new List<Stream>();
        try
        {
            var uploads = form.Photos.Select(file =>
            {
                var stream = file.OpenReadStream(); streams.Add(stream);
                return new PhotoUpload(stream, file.Length);
            }).ToList();
            return Ok(await service.AddPhotosAsync(UserId, id, form.Version, uploads, ct));
        }
        finally { foreach (var stream in streams) await stream.DisposeAsync(); }
    }
    [HttpGet("{id:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> Download(Guid id, Guid photoId, CancellationToken ct)
    {
        var photo = await service.DownloadAsync(UserId, id, photoId, ct);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(photo.Content, photo.ContentType, photo.FileName);
    }
}
