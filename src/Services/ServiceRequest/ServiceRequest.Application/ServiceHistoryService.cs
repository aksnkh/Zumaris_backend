using System.Collections.Concurrent;
namespace Zumaris.ServiceRequests.Application;

public sealed class ServiceHistoryService(IServiceHistoryRepository repository,IHistoryReferenceLookup references):IServiceHistoryService
{
    public async Task<PagedResult<ServiceHistoryDto>> GetHistoryAsync(Guid userId,HistoryFilterRequest request,CancellationToken ct,string? bearer=null)
    {
        if(userId==Guid.Empty)throw new UnauthorizedAccessException();
        var status=request.Status?.Trim().ToLowerInvariant() switch {
            null or "" or "all"=>null,"completed"=>"Completed","cancelled"=>"Cancelled","paid"=>"Paid",
            _=>throw new RequestValidationException(new(){["status"]=["Use All, Completed, Cancelled or Paid."]})
        };
        if(request.PageNumber<1||request.PageSize is <1 or >100)
            throw new RequestValidationException(new(){["pagination"]=["pageNumber must be positive; pageSize must be between 1 and 100."]});
        var page=await repository.GetHistoryAsync(userId,request with {Status=status},ct);
        return page with {Items=await Enrich(page.Items,bearer,ct)};
    }
    private async Task<IReadOnlyList<ServiceHistoryDto>> Enrich(IReadOnlyList<ServiceHistoryDto> items,string? bearer,CancellationToken ct)
    {
        if(items.Count==0)return items;
        var categories=new ConcurrentDictionary<int,HistoryCategoryReference>();
        var addresses=new ConcurrentDictionary<Guid,string>();var names=new ConcurrentDictionary<Guid,string>();
        IReadOnlyDictionary<int,string> areas=new Dictionary<int,string>();
        var jobs=new List<Func<CancellationToken,Task>>();
        foreach(var id in items.Select(i=>i.CategoryId).Where(id=>id>0).Distinct())
            jobs.Add(async token=>{var value=await references.CategoryAsync(id,token);if(value is not null)categories[id]=value;});
        if(items.Any(i=>i.AreaTypeId>0&&string.IsNullOrWhiteSpace(i.ServiceArea)))
            jobs.Add(async token=>areas=await references.AreasAsync(bearer,token));
        foreach(var id in items.Where(i=>string.IsNullOrWhiteSpace(i.PropertyAddress)).Select(i=>i.PropertyId).Where(id=>id!=Guid.Empty).Distinct())
            jobs.Add(async token=>{var value=await references.PropertyAddressAsync(id,bearer,token);if(!string.IsNullOrWhiteSpace(value))addresses[id]=value;});
        foreach(var id in items.Where(i=>string.IsNullOrWhiteSpace(i.TechnicianName)&&i.TechnicianId.HasValue).Select(i=>i.TechnicianId!.Value).Distinct())
            jobs.Add(async token=>{var value=await references.ProviderNameAsync(id,bearer,token);if(!string.IsNullOrWhiteSpace(value))names[id]=value;});
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct);budget.CancelAfter(TimeSpan.FromSeconds(3));
        using var slots=new SemaphoreSlim(4);
        async Task Run(Func<CancellationToken,Task> job)
        {
            var entered=false;
            try{await slots.WaitAsync(budget.Token);entered=true;await job(budget.Token);}
            catch(OperationCanceledException)when(!ct.IsCancellationRequested){}
            finally{if(entered)slots.Release();}
        }
        await Task.WhenAll(jobs.Select(Run));
        return items.Select(item=>{
            categories.TryGetValue(item.CategoryId,out var category);
            var categoryName=Present(item.CategoryName,category?.Name);
            var area=Present(item.ServiceArea,areas.GetValueOrDefault(item.AreaTypeId));
            var icon=Present(category?.IconUrl,item.CategoryIconUrl,item.CategoryIcon);
            return item with {CategoryName=categoryName,ServiceArea=area,CategoryIcon=icon,CategoryIconUrl=icon,
                ServiceName=categoryName,
                PropertyAddress=Present(item.PropertyAddress,addresses.GetValueOrDefault(item.PropertyId)),
                TechnicianName=Present(item.TechnicianName,item.TechnicianId.HasValue?names.GetValueOrDefault(item.TechnicianId.Value):null)};
        }).ToArray();
    }
    private static string? Present(params string?[] values)=>values.FirstOrDefault(v=>!string.IsNullOrWhiteSpace(v));
    public async Task<ServiceHistoryDetailDto?> GetByIdAsync(Guid userId,Guid historyId,CancellationToken ct,string? bearer=null)
    {
        if(userId==Guid.Empty)throw new UnauthorizedAccessException();
        var item=await repository.GetByIdAsync(userId,historyId,ct);if(item is null)return null;
        var row=new ServiceHistoryDto(item.RequestId,item.RequestNumber,item.ServiceName,item.CategoryName,item.CategoryIconUrl,item.ServiceArea,item.TechnicianName,
            item.ServiceDate,item.Status,item.PaymentStatus,item.TotalAmount,item.CurrencyCode,item.CreatedAt){CategoryId=item.CategoryId,AreaTypeId=item.AreaTypeId,
            PropertyId=item.PropertyId,PropertyAddress=item.PropertyAddress,TechnicianId=item.TechnicianId};
        var enriched=(await Enrich(new[]{row},bearer,ct))[0];
        return item with {CategoryName=enriched.CategoryName,ServiceName=enriched.ServiceName,ServiceArea=enriched.ServiceArea,TechnicianName=enriched.TechnicianName,
            PropertyAddress=enriched.PropertyAddress,CategoryIconUrl=enriched.CategoryIconUrl};
    }
}
