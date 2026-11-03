using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zumaris.Provider.Application;
namespace Zumaris.Provider.Api.Controllers;
[ApiController]
[Authorize]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
[Route("api/providers")]
public sealed class ProviderQuoteController(IProviderQuoteRepository providers):ControllerBase
{
    [HttpGet("{providerId:guid}/summary")]
    public async Task<IActionResult> Summary(Guid providerId,CancellationToken ct)
    {
        var item=await providers.SummaryAsync(providerId,ct);return item is null?NotFound():Ok(item);
    }
    [HttpGet("me/quote-profile")]
    public async Task<IActionResult> Mine([FromQuery,Range(1,int.MaxValue)]int categoryId,CancellationToken ct)
    {
        var item=await providers.GetAsync(Guid.Parse(User.FindFirst("sub")!.Value),null,categoryId,ct);
        return item is null?NotFound():Ok(item);
    }
    // Public business identity, accessible to authenticated customers for quote eligibility checks.
    [HttpGet("{providerId:guid}/quote-profile")]
    public async Task<IActionResult> Public(Guid providerId,[FromQuery,Range(1,int.MaxValue)]int categoryId,CancellationToken ct)
    {
        var item=await providers.GetAsync(null,providerId,categoryId,ct);
        return item is null?NotFound():Ok(item);
    }
}
