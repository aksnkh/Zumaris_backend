using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zumaris.Property.Application.Features;

namespace Zumaris.Property.Api.Controllers;

public sealed class PropertyExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        context.Result=context.Exception switch
        {
            PropertyValidationException ex => new BadRequestObjectResult(new ValidationProblemDetails(ex.Errors) { Status=400 }),
            PropertyNotFoundException => new NotFoundObjectResult(new ProblemDetails { Status=404,Title="Property not found." }),
            PropertyForbiddenException ex => new ObjectResult(new ProblemDetails { Status=403,Title=ex.Message }) { StatusCode=403 },
            PropertyConflictException ex => new ConflictObjectResult(new ProblemDetails { Status=409,Title=ex.Message }),
            _ => null
        };
        if(context.Result is not null)context.ExceptionHandled=true;
    }
}
[ApiController]
[Authorize]
[ServiceFilter(typeof(PropertyExceptionFilter))]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/properties")]
[RequestSizeLimit(16384)]
public sealed class PropertiesController(IPropertyService service) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);
    [HttpPost]
    [ProducesResponseType(typeof(PropertyDto),StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateProperty input,CancellationToken ct)
    {
        var result=await service.CreateAsync(UserId,input,ct);
        return Created($"properties/{result.PropertyId}",result);
    }
    [HttpGet]
    [ProducesResponseType(typeof(PropertyPage),StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery]int pageNumber=1,[FromQuery]int pageSize=20,CancellationToken ct=default) =>
        Ok(await service.ListAsync(UserId,pageNumber,pageSize,ct));
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PropertyDto),StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id,CancellationToken ct) => Ok(await service.GetAsync(UserId,id,ct));
    [HttpGet("current")]
    [ProducesResponseType(typeof(PropertyDto),StatusCodes.Status200OK)]
    public async Task<IActionResult> Current(CancellationToken ct) => Ok(await service.GetCurrentAsync(UserId,ct));
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PropertyDto),StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id,UpdateProperty input,CancellationToken ct) =>
        Ok(await service.UpdateAsync(UserId,id,input,ct));
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid id,[FromQuery]int version,CancellationToken ct)
    {
        await service.ArchiveAsync(UserId,id,version,ct);return NoContent();
    }
    [HttpPut("{id:guid}/current")]
    [ProducesResponseType(typeof(PropertyDto),StatusCodes.Status200OK)]
    public async Task<IActionResult> SelectCurrent(Guid id,CancellationToken ct) => Ok(await service.SetCurrentAsync(UserId,id,ct));
}

[ApiController]
[Authorize]
[Route("api/property-options")]
public sealed class PropertyOptionsController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PropertyOptions),StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new PropertyOptions(
        [new("Terraced","Terraced"),new("SemiDetached","Semi-detached"),new("Detached","Detached"),
         new("FlatApartment","Flat / Apartment"),new("Bungalow","Bungalow"),new("Other","Other")],
        [new("Owner","Owner"),new("Tenant","Tenant"),new("Landlord","Landlord"),new("PropertyManager","Property Manager")],
        PropertyRules.Countries));
}
