using Zumaris.ServiceRequests.Application.Quotes;
namespace Zumaris.ServiceRequests.Application;
public sealed record TechnicianEligibility(Guid ProviderId,Guid UserId,IReadOnlyList<int> CategoryIds);
public sealed record TechnicianJobs(IReadOnlyList<TechnicianJob> Items,int PageNumber,int PageSize,long TotalCount);
public sealed record OpportunityPage(IReadOnlyList<ProviderQuoteContext> Items,int PageNumber,int PageSize,long TotalCount);
public sealed record TechnicianJob(Guid RequestId,string RequestNumber,Guid PropertyId,string? PropertyAddress,int CategoryId,int AreaTypeId,
 string Description,string Status,DateOnly? PreferredDate,TimeOnly? PreferredStartTime,TimeOnly? PreferredEndTime,string TimeZoneId,
 Guid ProviderId,Guid QuoteId,decimal Amount,string CurrencyCode,DateTime? StartedAtUtc,DateTime? CompletedAtUtc,string? WorkReport,
 IReadOnlyList<RequestPhotoDto> RequestPhotos,IReadOnlyList<RequestPhotoDto> ResultPhotos);
public sealed record CompletedWorkContext(Guid RequestId,Guid CustomerId,Guid ProviderId,string Status,Guid QuoteId,decimal Amount,string CurrencyCode);
public interface ITechnicianEligibilityClient
{
 Task<TechnicianEligibility> GetAsync(Guid actor,string bearer,CancellationToken ct);
}
public interface ITechnicianWorkRepository
{
 Task<OpportunityPage> OpportunitiesAsync(Guid actor,IReadOnlyList<int> categories,int page,int size,CancellationToken ct);
 Task<TechnicianJobs> JobsAsync(Guid actor,string? status,int page,int size,CancellationToken ct);
 Task<TechnicianJob?> JobAsync(Guid actor,Guid id,bool customerAllowed,CancellationToken ct);
 Task StartAsync(Guid actor,Guid id,DateTime now,CancellationToken ct);
 Task CompleteAsync(Guid actor,Guid id,string report,IReadOnlyList<StoredPhoto> photos,DateTime now,CancellationToken ct);
 Task<CompletedWorkContext?> CompletedWorkAsync(Guid actor,Guid id,CancellationToken ct);
 Task<int> CompletedJobsAsync(Guid provider,CancellationToken ct);
 Task<PhotoRecord?> PhotoAsync(Guid actor,Guid requestId,Guid photoId,CancellationToken ct);
}
public sealed class TechnicianWorkService(ITechnicianWorkRepository repository,ITechnicianEligibilityClient eligibility,IPhotoStorage storage,TimeProvider clock)
{
 public async Task<OpportunityPage> OpportunitiesAsync(Guid actor,string bearer,int page,int size,CancellationToken ct)
 {
  Page(page,size);var p=await eligibility.GetAsync(actor,bearer,ct);
  return await repository.OpportunitiesAsync(actor,p.CategoryIds,page,size,ct);
 }
 public Task<TechnicianJobs> JobsAsync(Guid actor,string? status,int page,int size,CancellationToken ct)
 {
  Page(page,size);
  if(status is not null&&status is not("Assigned" or "InProgress" or "Completed" or "Cancelled"))throw new RequestValidationException(new(){["status"]=["Use Assigned, InProgress, Completed or Cancelled."]});
  return repository.JobsAsync(actor,status,page,size,ct);
 }
 public async Task<TechnicianJob> JobAsync(Guid actor,Guid id,bool customer,CancellationToken ct)=>await repository.JobAsync(actor,id,customer,ct)??throw new RequestNotFoundException();
 public async Task<TechnicianJob> StartAsync(Guid actor,Guid id,CancellationToken ct)
 {await repository.StartAsync(actor,id,clock.GetUtcNow().UtcDateTime,ct);return await JobAsync(actor,id,false,ct);}
 public async Task<TechnicianJob> CompleteAsync(Guid actor,Guid id,string report,IReadOnlyList<PhotoUpload> photos,CancellationToken ct)
 {
  if(string.IsNullOrWhiteSpace(report)||report.Trim().Length>10000)throw new RequestValidationException(new(){["report"]=["A work report of 1 to 10000 characters is required."]});
  if(photos.Count is <1 or >8)throw new RequestValidationException(new(){["photos"]=["Provide 1 to 8 result photos."]});
  var job=await JobAsync(actor,id,false,ct);
  if(job.Status!="InProgress")throw new RequestConflictException("Only an InProgress job can be completed.");
  var saved=new List<StoredPhoto>();bool committed=false;
  try
  {
   foreach(var photo in photos)saved.Add(await storage.SaveAsync(id,photo,ct));
   await repository.CompleteAsync(actor,id,report.Trim(),saved,clock.GetUtcNow().UtcDateTime,ct);committed=true;
   return await JobAsync(actor,id,false,ct);
  }
  finally {if(!committed)foreach(var file in saved)await storage.DeleteAsync(file.FilePath);}
 }
 public async Task<CompletedWorkContext> CompletedAsync(Guid actor,Guid id,CancellationToken ct)=>await repository.CompletedWorkAsync(actor,id,ct)??throw new RequestNotFoundException();
 public async Task<PhotoDownload> PhotoAsync(Guid actor,Guid id,Guid photo,CancellationToken ct)
 {var item=await repository.PhotoAsync(actor,id,photo,ct)??throw new RequestNotFoundException();return await storage.OpenAsync(item.FilePath,ct);}
 private static void Page(int page,int size)
 {if(page<1||size is <1 or >100)throw new RequestValidationException(new(){["pagination"]=["Use a positive pageNumber and pageSize from 1 to 100."]});}
}
