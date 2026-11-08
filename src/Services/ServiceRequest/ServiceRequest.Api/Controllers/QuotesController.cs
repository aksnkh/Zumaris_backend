using Zumaris.BuildingBlocks.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zumaris.BuildingBlocks.Common.Responses;
using Zumaris.ServiceRequests.Application;
using Zumaris.ServiceRequests.Application.Quotes;
namespace Zumaris.ServiceRequests.Api.Controllers;

public sealed class QuoteExceptionFilter:IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        context.Result=context.Exception switch {
            RequestValidationException ex=>new BadRequestObjectResult(new ValidationProblemDetails(ex.Errors){Status=400}),
            QuoteForbiddenException ex=>Error(403,ex.Message),QuoteNotFoundException ex=>Error(404,ex.Message),
            QuoteConflictException ex=>Error(409,ex.Message),QuoteUnavailableException ex=>Error(503,ex.Message),_=>null
        };
        if(context.Result is not null)context.ExceptionHandled=true;
    }
    private static ObjectResult Error(int status,string message)=>new(new ProblemDetails{Status=status,Title=message,Extensions={ ["message"]=message,["statusCode"]=status }}){StatusCode=status};
}
[ApiController]
[Authorize]
[ServiceFilter(typeof(QuoteExceptionFilter))]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/quotes")]
public sealed class QuotesController(QuoteService quotes):ControllerBase
{
    private Guid UserId=>Guid.Parse(User.FindFirst("sub")!.Value);
    private string Bearer=>Request.Headers.Authorization.ToString()["Bearer ".Length..];
    private static Response<T> Success<T>(T payload,string message)=>new(payload){StatusCode=200,Title="Success",Message=message,MessageLevel=MessageLevel.Cif};
    [HttpGet("/api/service-requests/{requestId:guid}/quote-context")]
    [ProducesResponseType(typeof(Response<ProviderQuoteContext>),200)]
    [ApplicationAccess("Tech")]
    public async Task<IActionResult> Context(Guid requestId,CancellationToken ct)=>
        Ok(Success(await quotes.ContextAsync(UserId,requestId,Bearer,ct),"Quote context retrieved successfully."));
    [HttpPost("/api/service-requests/{requestId:guid}/quotes")]
    [RequestSizeLimit(16384)]
    [ProducesResponseType(typeof(Response<QuoteDto>),201)]
    [ApplicationAccess("Tech")]
    public async Task<IActionResult> Submit(Guid requestId,CreateQuoteRequest input,CancellationToken ct)
    {
        var item=await quotes.CreateAsync(UserId,requestId,input,Bearer,ct);
        var response=Success(item,"Quote submitted successfully.");response.StatusCode=201;response.Title="Created";
        return Created($"../../quotes/{item.Id}",response);
    }
    [HttpGet]
    [ProducesResponseType(typeof(Response<QuotePage>),200)]
    public async Task<IActionResult> List([FromQuery]string? status="Pending",[FromQuery]Guid? propertyId=null,[FromQuery]int pageNumber=1,[FromQuery]int pageSize=20,CancellationToken ct=default)=>
        Ok(Success(await quotes.ListAsync(UserId,false,status,propertyId,pageNumber,pageSize,ct),"Quotes retrieved successfully."));
    [HttpGet("sent")]
    [ProducesResponseType(typeof(Response<QuotePage>),200)]
    [ApplicationAccess("Tech")]
    public async Task<IActionResult> Sent([FromQuery]string? status=null,[FromQuery]int pageNumber=1,[FromQuery]int pageSize=20,CancellationToken ct=default)=>
        Ok(Success(await quotes.ListAsync(UserId,true,status,null,pageNumber,pageSize,ct),"Sent quotes retrieved successfully."));
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<QuoteDto>),200)]
    [ApplicationAccess("App", "Tech")]
    public async Task<IActionResult> Details(Guid id,CancellationToken ct)=>Ok(Success(await quotes.GetAsync(UserId,id,ct,User.FindFirst("application")!.Value=="Tech"),"Quote retrieved successfully."));
    [HttpPost("{id:guid}/accept")]
    [ProducesResponseType(typeof(Response<QuoteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Accept(Guid id,CancellationToken ct)=>Ok(Success(await quotes.DecideAsync(UserId,id,"Accepted",Bearer,ct),"Quote accepted successfully."));
    [HttpPost("{id:guid}/decline")]
    [ProducesResponseType(typeof(Response<QuoteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Decline(Guid id,CancellationToken ct)=>Ok(Success(await quotes.DecideAsync(UserId,id,"Declined",Bearer,ct),"Quote declined successfully."));
    [HttpPost("{id:guid}/withdraw")]
    [ProducesResponseType(typeof(Response<QuoteDto>), StatusCodes.Status200OK)]
    [ApplicationAccess("Tech")]
    public async Task<IActionResult> Withdraw(Guid id,CancellationToken ct)=>Ok(Success(await quotes.DecideAsync(UserId,id,"Withdrawn",Bearer,ct),"Quote withdrawn successfully."));
}
