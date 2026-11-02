using System.ComponentModel.DataAnnotations;
using Identity.Api.Security;
using Identity.Application.Features.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zumaris.BuildingBlocks.Common.Responses;
namespace Identity.Api.Controllers;

public sealed class ProfileExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            ProfileValidationException ex => new BadRequestObjectResult(new ValidationProblemDetails(ex.Errors) { Status = 400 }),
            ProfileNotFoundException ex => Error(404,ex.Message),
            _ => null
        };
        if (context.Result is not null) context.ExceptionHandled = true;
    }
    private static ObjectResult Error(int status,string message) => new(new ProblemDetails {
        Status=status, Title=message, Extensions={ ["message"]=message,["statusCode"]=status }
    }) { StatusCode=status };
}
public sealed class UploadProfilePhotoForm
{
    [Required] public IFormFile Photo { get; set; } = null!;
}

[ApiController]
[Authorize]
[AuthRequest]
[ServiceFilter(typeof(ProfileExceptionFilter))]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/profile/me")]
public sealed class ProfileController(ProfileService profiles) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);
    private static Response<ProfileResponse> Success(ProfileResponse profile,string message) => new(profile) {
        StatusCode=200,Title="Success",Message=message,MessageLevel=MessageLevel.Cif
    };
    [HttpGet]
    [ProducesResponseType(typeof(Response<ProfileResponse>),200)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(Success(await profiles.GetAsync(UserId,ct),"Profile retrieved successfully."));

    [HttpPut]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(Response<ProfileResponse>),200)]
    public async Task<IActionResult> Update(UpdateProfileRequest request,CancellationToken ct) =>
        Ok(Success(await profiles.UpdateAsync(UserId,request,ct),"Profile updated successfully."));

    [HttpPost("photo")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit=6 * 1024 * 1024)]
    [ProducesResponseType(typeof(Response<ProfileResponse>),200)]
    public async Task<IActionResult> Upload([FromForm] UploadProfilePhotoForm form,CancellationToken ct)
    {
        await using var stream = form.Photo.OpenReadStream();
        return Ok(Success(await profiles.UploadPhotoAsync(UserId,new(stream,form.Photo.Length),ct),"Profile photo updated successfully."));
    }
    [HttpGet("photo")]
    public async Task<IActionResult> Photo(CancellationToken ct)
    {
        var photo = await profiles.DownloadPhotoAsync(UserId,ct);
        Response.Headers.XContentTypeOptions="nosniff";
        return File(photo.Content,photo.ContentType);
    }
    [HttpDelete("photo")]
    [ProducesResponseType(typeof(Response<ProfileResponse>),200)]
    public async Task<IActionResult> DeletePhoto(CancellationToken ct) =>
        Ok(Success(await profiles.DeletePhotoAsync(UserId,ct),"Profile photo removed successfully."));
}
