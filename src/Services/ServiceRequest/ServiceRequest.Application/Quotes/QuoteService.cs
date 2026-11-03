using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Zumaris.ServiceRequests.Application.Quotes;

public sealed class QuoteService(IQuoteRepository repository,IProviderQuoteEligibility providers,IQuoteDisplayLookup display,TimeProvider clock)
{
    public async Task<ProviderQuoteContext> ContextAsync(Guid actor,Guid requestId,string bearer,CancellationToken ct)
    {
        var request=await repository.ContextAsync(requestId,ct)??throw new QuoteNotFoundException();
        if(request.UserId==actor)throw new QuoteForbiddenException();
        await providers.GetAsync(actor,request.CategoryId,bearer,ct);
        if(request.Status!="Submitted")throw new QuoteConflictException("Only Submitted requests can receive quotes.");
        var labels=await display.GetAsync(request.CategoryId,request.AreaTypeId,bearer,ct);
        return new(request.RequestId,request.RequestNumber,request.CategoryId,request.AreaTypeId,request.Description,labels.CategoryName??request.CategoryName,
            labels.AreaName??request.AreaName,request.PreferredDate,request.PreferredStartTime,request.PreferredEndTime,request.TimeZoneId);
    }
    public async Task<QuoteDto> CreateAsync(Guid actor,Guid requestId,CreateQuoteRequest request,string bearer,CancellationToken ct)
    {
        var errors=new Dictionary<string,string[]>();
        if(request.LabourAmount<0 || request.CalloutAmount<0 || request.LabourAmount>999999.99m || request.CalloutAmount>999999.99m ||
            decimal.Round(request.LabourAmount,2)!=request.LabourAmount || decimal.Round(request.CalloutAmount,2)!=request.CalloutAmount)
            errors["amount"]=["Amounts must be nonnegative, at most 999999.99, and use no more than two decimal places."];
        var total=request.LabourAmount is >=0 and <=999999.99m && request.CalloutAmount is >=0 and <=999999.99m ? request.LabourAmount+request.CalloutAmount : 0;
        if(total is <=0 or >999999.99m)
            errors["totalAmount"]=["The calculated total must be greater than zero and at most 999999.99."];
        if(request.CurrencyCode!="GBP") errors["currencyCode"]=["Only GBP quotes are supported."];
        var note=string.IsNullOrWhiteSpace(request.ProviderNote)?null:request.ProviderNote.Trim();
        var summary=string.IsNullOrWhiteSpace(request.PriceSummary)?"Includes labour and call-out. Additional parts require customer approval.":request.PriceSummary.Trim();
        if((note?.EnumerateRunes().Count()??0)>2000)errors["providerNote"]=["Provider note must not exceed 2000 characters."];
        if(summary.EnumerateRunes().Count()>500)errors["priceSummary"]=["Price summary must not exceed 500 characters."];
        var now=clock.GetUtcNow().UtcDateTime;
        if(request.ExpiresAtUtc is {} expiry && (expiry.Kind!=DateTimeKind.Utc || expiry<=now))
            errors["expiresAtUtc"]=["Expiry must be a future UTC timestamp ending in Z."];
        if(errors.Count>0)throw new RequestValidationException(errors);
        var context=await repository.ContextAsync(requestId,ct)??throw new QuoteNotFoundException();
        if(context.UserId==actor)throw new QuoteForbiddenException();
        var provider=await providers.GetAsync(actor,context.CategoryId,bearer,ct);
        var normalized=request with {ProviderNote=note,PriceSummary=summary};
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {Labour=normalized.LabourAmount.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture),Callout=normalized.CalloutAmount.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture),normalized.CurrencyCode,normalized.ProviderNote,normalized.PriceSummary,normalized.ExpiresAtUtc})))).ToLowerInvariant();
        var labels=await display.GetAsync(context.CategoryId,context.AreaTypeId,bearer,ct);
        return await repository.CreateAsync(actor,requestId,normalized,provider,labels,context.RequestVersion,hash,now,ct);
    }
    public Task<QuotePage> ListAsync(Guid actor,bool sent,string? status,Guid? propertyId,int page,int size,CancellationToken ct)
    {
        status=status?.Trim();
        string? normalized=string.IsNullOrWhiteSpace(status)||status.Equals("All",StringComparison.OrdinalIgnoreCase)?null:
            new[]{"Pending","Accepted","Declined","Withdrawn","Expired"}.FirstOrDefault(s=>s.Equals(status.Trim(),StringComparison.OrdinalIgnoreCase));
        if(!string.IsNullOrWhiteSpace(status)&&!status.Equals("All",StringComparison.OrdinalIgnoreCase)&&normalized is null)
            throw new RequestValidationException(new(){["status"]=["Use All, Pending, Accepted, Declined, Withdrawn or Expired."]});
        if(page<1||size is <1 or >100 || propertyId==Guid.Empty)
            throw new RequestValidationException(new(){["pagination"]=["Use positive pageNumber, pageSize 1–100 and a nonempty propertyId when supplied."]});
        return repository.ListAsync(actor,sent,normalized,propertyId,page,size,clock.GetUtcNow().UtcDateTime,ct);
    }
    public async Task<QuoteDto> GetAsync(Guid actor,Guid id,CancellationToken ct)=>
        await repository.GetAsync(actor,id,clock.GetUtcNow().UtcDateTime,ct)??throw new QuoteNotFoundException();
    public async Task<QuoteDto> DecideAsync(Guid actor,Guid id,string decision,string bearer,CancellationToken ct)
    {
        if(decision=="Accepted")
        {
            var current=await GetAsync(actor,id,ct);
            if(current.Status=="Pending")await providers.VerifyAsync(current.ProviderId,current.CategoryId,bearer,ct);
        }
        return await repository.DecideAsync(actor,id,decision,clock.GetUtcNow().UtcDateTime,ct);
    }
}
