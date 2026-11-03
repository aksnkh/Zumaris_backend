using System.Data;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.ServiceRequests.Application;
using Zumaris.ServiceRequests.Application.Quotes;
namespace Zumaris.ServiceRequests.Infrastructure;
public sealed class TechnicianWorkRepository(IConfiguration configuration):ITechnicianWorkRepository
{
 private async Task<MySqlConnection> Open(CancellationToken ct)
 {var c=new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));try{await c.OpenAsync(ct);return c;}catch{await c.DisposeAsync();throw;}}
 private static MySqlCommand Cmd(MySqlConnection c,MySqlTransaction? tx,string sql,params(string,object?)[] values)
 {var cmd=new MySqlCommand(sql,c,tx);foreach(var(k,v)in values)cmd.Parameters.AddWithValue(k,v??DBNull.Value);return cmd;}
 private const string Join=" FROM service_requests r JOIN service_request_quotes q ON q.request_id=r.id AND q.status='Accepted' AND q.provider_id=r.technician_id ";
 private static string? Text(MySqlDataReader r,string n)=>r.IsDBNull(r.GetOrdinal(n))?null:r.GetString(n);
 private static DateTime? Date(MySqlDataReader r,string n)=>r.IsDBNull(r.GetOrdinal(n))?null:DateTime.SpecifyKind(r.GetDateTime(n),DateTimeKind.Utc);
 private static DateOnly? Day(MySqlDataReader r)=>r.IsDBNull(r.GetOrdinal("preferred_date"))?null:DateOnly.FromDateTime(r.GetDateTime("preferred_date"));
 private static TimeOnly? Time(MySqlDataReader r,string n)=>r.IsDBNull(r.GetOrdinal(n))?null:TimeOnly.FromTimeSpan(r.GetTimeSpan(n));
 public async Task<OpportunityPage> OpportunitiesAsync(Guid actor,IReadOnlyList<int> categories,int page,int size,CancellationToken ct)
 {
  if(categories.Count==0)return new([],page,size,0);
  await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
  var names=categories.Distinct().Select((_,i)=>"@cat"+i).ToArray();
  string filter="status='Submitted' AND user_id<>@actor AND category_id IN ("+string.Join(',',names)+")";
  MySqlCommand Query(string sql)
  {
   var cmd=Cmd(c,tx,sql,("@actor",actor.ToString()),("@size",size),("@offset",(long)(page-1)*size));var list=categories.Distinct().ToArray();for(int i=0;i<names.Length;i++)cmd.Parameters.AddWithValue(names[i],list[i]);return cmd;
  }
  await using var count=Query("SELECT COUNT(*) FROM service_requests WHERE "+filter);var total=Convert.ToInt64(await count.ExecuteScalarAsync(ct));
  await using var cmd=Query("SELECT id,request_number,category_id,area_type_id,description,category_name,service_area,preferred_date,preferred_start_time,preferred_end_time,time_zone_id FROM service_requests WHERE "+filter+" ORDER BY created_at DESC,id DESC LIMIT @size OFFSET @offset");
  var items=new List<ProviderQuoteContext>();await using(var r=await cmd.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))items.Add(new(r.GetGuid(0),r.GetString(1),r.GetInt32(2),r.GetInt32(3),r.GetString(4),Text(r,"category_name"),Text(r,"service_area"),Day(r),Time(r,"preferred_start_time"),Time(r,"preferred_end_time"),r.GetString("time_zone_id")));
  await tx.CommitAsync(ct);return new(items,page,size,total);
 }
 public async Task<TechnicianJobs> JobsAsync(Guid actor,string? status,int page,int size,CancellationToken ct)
 {
  await using var c=await Open(ct);
  const string filter=" WHERE q.provider_user_id=@actor AND (@status IS NULL OR r.status=@status)";
  await using var count=Cmd(c,null,"SELECT COUNT(*)"+Join+filter,("@actor",actor.ToString()),("@status",status));var total=Convert.ToInt64(await count.ExecuteScalarAsync(ct));
  await using var cmd=Cmd(c,null,"SELECT r.id"+Join+filter+" ORDER BY r.created_at DESC,r.id DESC LIMIT @size OFFSET @offset",("@actor",actor.ToString()),("@status",status),("@size",size),("@offset",(long)(page-1)*size));
  var ids=new List<Guid>();await using(var r=await cmd.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))ids.Add(r.GetGuid(0));
  var list=new List<TechnicianJob>();foreach(var id in ids){var job=await JobAsync(actor,id,false,ct);if(job is not null)list.Add(job);}return new(list,page,size,total);
 }
 public async Task<TechnicianJob?> JobAsync(Guid actor,Guid id,bool customer,CancellationToken ct)
 {
  await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
  await using var cmd=Cmd(c,tx,"SELECT r.*,q.id AS quote_id,q.total_amount AS quote_amount,q.currency_code AS quote_currency,e.started_at,e.completed_at,e.work_report"+Join+" LEFT JOIN service_request_execution e ON e.request_id=r.id WHERE r.id=@id AND (q.provider_user_id=@actor OR (@customer=1 AND r.user_id=@actor))",("@actor",actor.ToString()),("@id",id.ToString()),("@customer",customer));
  TechnicianJob job;
  await using(var r=await cmd.ExecuteReaderAsync(ct))
  {
   if(!await r.ReadAsync(ct))return null;
   job=new(r.GetGuid(r.GetOrdinal("id")),r.GetString("request_number"),r.GetGuid(r.GetOrdinal("property_id")),Text(r,"property_address"),r.GetInt32("category_id"),r.GetInt32("area_type_id"),r.GetString("description"),r.GetString("status"),Day(r),Time(r,"preferred_start_time"),Time(r,"preferred_end_time"),r.GetString("time_zone_id"),r.GetGuid(r.GetOrdinal("technician_id")),r.GetGuid(r.GetOrdinal("quote_id")),r.GetDecimal("quote_amount"),r.GetString("quote_currency"),Date(r,"started_at"),Date(r,"completed_at"),Text(r,"work_report"),[],[]);
  }
  async Task<IReadOnlyList<RequestPhotoDto>> Photos(string table,string route)
  {
   var result=new List<RequestPhotoDto>();await using var q=Cmd(c,tx,"SELECT id FROM "+table+" WHERE request_id=@id ORDER BY id",("@id",id.ToString()));
   await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct)){var photo=r.GetGuid(0);result.Add(new(photo,$"/service-request/api/technician/jobs/{id}/{route}/{photo}"));}return result;
  }
  job=job with {RequestPhotos=await Photos("service_request_photos","photos"),ResultPhotos=await Photos("service_request_result_photos","photos")};
  await tx.CommitAsync(ct);return job;
 }
 private static async Task<(string Status,int Version)> Lock(MySqlConnection c,MySqlTransaction tx,Guid actor,Guid id,CancellationToken ct)
 {
  // Match Quote acceptance lock order: request row first, accepted quote second.
  string status;int version;string? provider;
  await using(var cmd=Cmd(c,tx,"SELECT status,version,technician_id FROM service_requests WHERE id=@id FOR UPDATE",("@id",id.ToString())))
  await using(var r=await cmd.ExecuteReaderAsync(ct))
  {if(!await r.ReadAsync(ct))throw new RequestNotFoundException();status=r.GetString(0);version=r.GetInt32(1);provider=r.IsDBNull(2)?null:r.GetGuid(2).ToString();}
  await using(var cmd=Cmd(c,tx,"SELECT id FROM service_request_quotes WHERE request_id=@id AND status='Accepted' AND provider_id=@provider AND provider_user_id=@actor FOR UPDATE",("@id",id.ToString()),("@provider",provider),("@actor",actor.ToString())))
   if(await cmd.ExecuteScalarAsync(ct) is null)throw new RequestNotFoundException();
  return(status,version);
 }
 private static async Task Event(MySqlConnection c,MySqlTransaction tx,Guid actor,Guid id,string from,string to,DateTime now,CancellationToken ct)
 {
  await using var cmd=Cmd(c,tx,"INSERT INTO service_request_status_history(id,request_id,from_status,to_status,actor_user_id,occurred_at,reason)VALUES(@event,@id,@from,@to,@actor,@now,NULL)",("@event",Guid.NewGuid().ToString()),("@id",id.ToString()),("@from",from),("@to",to),("@actor",actor.ToString()),("@now",now));await cmd.ExecuteNonQueryAsync(ct);
 }
 public async Task StartAsync(Guid actor,Guid id,DateTime now,CancellationToken ct)
 {
  await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(ct);var current=await Lock(c,tx,actor,id,ct);
  if(current.Status is "InProgress" or "Completed"){await tx.CommitAsync(ct);return;}
  if(current.Status!="Assigned")throw new RequestConflictException("Only an Assigned job can be started.");
  await using(var cmd=Cmd(c,tx,"INSERT INTO service_request_execution(request_id,technician_user_id,started_at)VALUES(@id,@actor,@now)",("@id",id.ToString()),("@actor",actor.ToString()),("@now",now)))await cmd.ExecuteNonQueryAsync(ct);
  await using(var cmd=Cmd(c,tx,"UPDATE service_requests SET status='InProgress',version=version+1,updated_at=@now WHERE id=@id",("@id",id.ToString()),("@now",now)))await cmd.ExecuteNonQueryAsync(ct);
  await Event(c,tx,actor,id,"Assigned","InProgress",now,ct);await tx.CommitAsync(ct);
 }
 public async Task CompleteAsync(Guid actor,Guid id,string report,IReadOnlyList<StoredPhoto> photos,DateTime now,CancellationToken ct)
 {
  if(string.IsNullOrWhiteSpace(report)||report.Length>10000||photos.Count is <1 or >8)throw new RequestValidationException(new(){["completion"]=["A work report and 1 to 8 result photos are required."]});
  await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(ct);var current=await Lock(c,tx,actor,id,ct);
  if(current.Status!="InProgress")throw new RequestConflictException("Only an InProgress job can be completed.");
  await using(var cmd=Cmd(c,tx,"UPDATE service_request_execution SET completed_at=@now,work_report=@report WHERE request_id=@id AND technician_user_id=@actor AND completed_at IS NULL",("@id",id.ToString()),("@actor",actor.ToString()),("@now",now),("@report",report)))if(await cmd.ExecuteNonQueryAsync(ct)!=1)throw new RequestConflictException("The job execution record has changed.");
  foreach(var photo in photos)
  {await using var cmd=Cmd(c,tx,"INSERT INTO service_request_result_photos(id,request_id,file_path,created_at)VALUES(@photo,@id,@path,@now)",("@photo",photo.Id.ToString()),("@id",id.ToString()),("@path",photo.FilePath),("@now",now));await cmd.ExecuteNonQueryAsync(ct);}
  await using(var cmd=Cmd(c,tx,"""
    UPDATE service_requests r JOIN service_request_quotes q ON q.request_id=r.id AND q.status='Accepted'
    SET r.status='Completed',r.service_date=@now,r.updated_at=@now,r.version=r.version+1,
        r.total_amount=q.total_amount,r.currency_code=q.currency_code
    WHERE r.id=@id
    """,("@id",id.ToString()),("@now",now)))await cmd.ExecuteNonQueryAsync(ct);
  await Event(c,tx,actor,id,"InProgress","Completed",now,ct);await tx.CommitAsync(ct);
 }
 public async Task<CompletedWorkContext?> CompletedWorkAsync(Guid actor,Guid id,CancellationToken ct)
 {
  await using var c=await Open(ct);await using var cmd=Cmd(c,null,"SELECT r.id,r.user_id,r.technician_id,r.status,q.id,q.total_amount,q.currency_code"+Join+" WHERE r.id=@id AND r.user_id=@actor",("@id",id.ToString()),("@actor",actor.ToString()));
  await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return null;
  if(r.GetString(3)!="Completed")throw new RequestConflictException("The job must be completed first.");
  return new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetString(3),r.GetGuid(4),r.GetDecimal(5),r.GetString(6));
 }
 public async Task<int> CompletedJobsAsync(Guid provider,CancellationToken ct)
 {
  await using var c=await Open(ct);await using var cmd=Cmd(c,null,"SELECT COUNT(*) FROM service_requests WHERE technician_id=@provider AND status='Completed'",("@provider",provider.ToString()));return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
 }
 public async Task<PhotoRecord?> PhotoAsync(Guid actor,Guid id,Guid photo,CancellationToken ct)
 {
  await using var c=await Open(ct);await using var cmd=Cmd(c,null,"""
    SELECT p.id,p.request_id,p.file_path,p.created_at FROM
    (SELECT id,request_id,file_path,created_at FROM service_request_photos WHERE request_id=@id
     UNION ALL SELECT id,request_id,file_path,created_at FROM service_request_result_photos WHERE request_id=@id) p
    JOIN service_requests r ON r.id=p.request_id JOIN service_request_quotes q ON q.request_id=r.id AND q.status='Accepted' AND q.provider_id=r.technician_id
    WHERE p.id=@photo AND (r.user_id=@actor OR q.provider_user_id=@actor)
    """,("@id",id.ToString()),("@photo",photo.ToString()),("@actor",actor.ToString()));
  await using var r=await cmd.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),0,DateTime.SpecifyKind(r.GetDateTime(3),DateTimeKind.Utc)):null;
 }
}
