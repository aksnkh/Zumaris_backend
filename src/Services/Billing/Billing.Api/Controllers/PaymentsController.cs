using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zumaris.Billing.Application;
namespace Zumaris.Billing.Api.Controllers;
public sealed class BillingFilter:IExceptionFilter
{
 public void OnException(ExceptionContext context)
 {
  if(context.Exception is not BillingException ex)return;
  context.Result=new ObjectResult(new{message=ex.Message,statusCode=ex.Status}){StatusCode=ex.Status};context.ExceptionHandled=true;
 }
}
[ApiController,Authorize,ServiceFilter(typeof(BillingFilter))]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/payments")]
public sealed class PaymentsController(PaymentService service,IPaymentRepository repository):ControllerBase
{
 private Guid Actor=>Guid.Parse(User.FindFirst("sub")!.Value);
 [HttpPost]
 public async Task<IActionResult> Create(CreatePaymentRequest input,CancellationToken ct)=>Ok(new{message="Payment request recorded. PayPal checkout is not connected yet.",payload=await service.CreateAsync(Actor,input,Request.Headers.Authorization.ToString()["Bearer ".Length..],ct)});
 [HttpGet("{id:guid}")]
 public async Task<IActionResult> Get(Guid id,CancellationToken ct)
 {var item=await repository.GetAsync(Actor,id,ct);return item is null?NotFound(new{message="Payment request not found."}):Ok(item);}
}
