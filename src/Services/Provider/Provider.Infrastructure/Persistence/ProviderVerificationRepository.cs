using System.Data;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Zumaris.Provider.Application;
namespace Zumaris.Provider.Infrastructure.Persistence;

public sealed class ProviderVerificationRepository(IConfiguration configuration):IProviderVerificationRepository
{
 private async Task<MySqlConnection> Open(CancellationToken ct)
 {var c=new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));try{await c.OpenAsync(ct);return c;}catch{await c.DisposeAsync();throw;}}
 private static MySqlCommand Cmd(MySqlConnection c,MySqlTransaction? tx,string sql,params(string,object?)[] values)
 {var cmd=new MySqlCommand(sql,c,tx);foreach(var(k,v)in values)cmd.Parameters.AddWithValue(k,v??DBNull.Value);return cmd;}
 private static ProviderWorkflowException Missing()=>new(404,"Provider or verification document not found.");
 private static ProviderWorkflowException Conflict(string message)=>new(409,message);
 private static async Task<Guid> Provider(MySqlConnection c,MySqlTransaction? tx,Guid? owner,Guid? provider,CancellationToken ct,bool locked=false)
 {
  if(owner is null&&provider is null)throw Missing();
  await using var cmd=Cmd(c,tx,"SELECT id FROM providers WHERE (@owner IS NULL OR user_id=@owner) AND (@id IS NULL OR id=@id)"+(locked?" FOR UPDATE":""),("@owner",owner?.ToString()),("@id",provider?.ToString()));
  var value=await cmd.ExecuteScalarAsync(ct);return value is null?throw Missing():Guid.Parse(value.ToString()!);
 }
 private static string? Text(MySqlDataReader r,string column)=>r.IsDBNull(r.GetOrdinal(column))?null:r.GetString(column);
 private static DateTime? Date(MySqlDataReader r,string column)=>r.IsDBNull(r.GetOrdinal(column))?null:DateTime.SpecifyKind(r.GetDateTime(column),DateTimeKind.Utc);
 private static string Display(VerificationDocumentType type)=>type switch
 {VerificationDocumentType.IdentityCheck=>"Identity check",VerificationDocumentType.Insurance=>"Insurance",_=>"Certificate"};
 private sealed record Slot(Guid Current,Guid? Verified,string? Reason);
 private sealed record Entry(VerificationDocumentType Type,VerificationFile File);
 private const string Files="""
  SELECT r.*,d.file_name,d.content_type,d.created_at
  FROM provider_document_reviews r JOIN provider_documents d ON d.id=r.document_id AND d.provider_id=r.provider_id
  """;
 private static VerificationFile File(MySqlDataReader r,Guid provider,bool admin)
 {
  var id=r.GetGuid(r.GetOrdinal("document_id"));
  var path=admin?$"/provider/api/admin/providers/{provider}/documents/{id}":$"/provider/api/providers/me/documents/{id}";
  return new(id,r.GetString("file_name"),r.GetString("content_type"),Date(r,"created_at")!.Value,r.GetString("status"),Date(r,"submitted_at"),Date(r,"reviewed_at"),Text(r,"review_feedback"),path,Text(r,"identity_kind") is string kind?Enum.Parse<IdentityDocumentKind>(kind):null);
 }
 private static async Task<(VerificationOverview Overview,IReadOnlyList<Entry> Files)> Read(MySqlConnection c,MySqlTransaction tx,Guid provider,bool admin,CancellationToken ct)
 {
  var slots=new Dictionary<VerificationDocumentType,Slot>();
  await using(var cmd=Cmd(c,tx,"SELECT document_type,current_document_id,verified_document_id,resubmission_reason FROM provider_verification_slots WHERE provider_id=@id",("@id",provider.ToString())))
  await using(var r=await cmd.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))slots.Add(Enum.Parse<VerificationDocumentType>(r.GetString(0)),new(r.GetGuid(1),r.IsDBNull(2)?null:r.GetGuid(2),r.IsDBNull(3)?null:r.GetString(3)));
  var files=new List<Entry>();
  await using(var cmd=Cmd(c,tx,Files+" WHERE r.provider_id=@id ORDER BY d.created_at DESC,r.document_id DESC",("@id",provider.ToString())))
  await using(var r=await cmd.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))files.Add(new(Enum.Parse<VerificationDocumentType>(r.GetString("document_type")),File(r,provider,admin)));
  var cards=new List<VerificationDocumentCard>();
  foreach(var type in Enum.GetValues<VerificationDocumentType>())
  {
   slots.TryGetValue(type,out var slot);var current=files.FirstOrDefault(e=>e.Type==type&&e.File.DocumentId==slot?.Current)?.File;
   var verified=files.FirstOrDefault(e=>e.Type==type&&e.File.DocumentId==slot?.Verified&&e.File.Status=="Verified")?.File;
   var status=current?.Status??"NotUploaded";
   if(slot?.Reason is not null&&verified is null&&status=="Verified")status="ActionRequired";
   cards.Add(new(type,Display(type),status,true,verified is not null,current?.ReviewFeedback??slot?.Reason,current,verified));
  }
  VerificationProfile profile=new(null,null);
  await using(var cmd=Cmd(c,tx,"SELECT postcode,other_skills FROM provider_verification_profile WHERE provider_id=@id",("@id",provider.ToString())))
  await using(var r=await cmd.ExecuteReaderAsync(ct))if(await r.ReadAsync(ct))profile=new(r.GetString(0),r.IsDBNull(1)?null:r.GetString(1));
  return(new(cards,profile),files);
 }

 public async Task<VerificationProfile> SaveProfileAsync(Guid owner,SaveVerificationProfile input,CancellationToken ct)
 {
  var postcode=input.Postcode?.Trim().ToUpperInvariant();
  if(string.IsNullOrWhiteSpace(postcode)||postcode.Length is <2 or >20||postcode.Any(ch=>!(char.IsAsciiLetterOrDigit(ch)||ch==' '||ch=='-')))
   throw new ProviderWorkflowException(400,"Provide a valid postcode using 2 to 20 letters, digits, spaces or hyphens.");
  var skills=string.IsNullOrWhiteSpace(input.OtherSkills)?null:input.OtherSkills.Trim();
  if(skills?.Length>1000)throw new ProviderWorkflowException(400,"Other skills must not exceed 1000 characters.");
  await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(ct);var id=await Provider(c,tx,owner,null,ct,true);
  await using(var cmd=Cmd(c,tx,"INSERT INTO provider_verification_profile(provider_id,postcode,other_skills,updated_at)VALUES(@id,@postcode,@skills,UTC_TIMESTAMP(6)) ON DUPLICATE KEY UPDATE postcode=@postcode,other_skills=@skills,updated_at=UTC_TIMESTAMP(6)",("@id",id.ToString()),("@postcode",postcode),("@skills",skills)))await cmd.ExecuteNonQueryAsync(ct);
  await using(var cmd=Cmd(c,tx,"UPDATE providers p SET is_verified=("+ProviderDocumentEligibility.Predicate+") WHERE p.id=@id",("@id",id.ToString())))await cmd.ExecuteNonQueryAsync(ct);
  await tx.CommitAsync(ct);return new(postcode,skills);
 }
 public async Task<VerificationOverview> OverviewAsync(Guid owner,CancellationToken ct)
 {
  await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);var id=await Provider(c,tx,owner,null,ct);var data=await Read(c,tx,id,false,ct);await tx.CommitAsync(ct);return data.Overview;
 }
 public async Task<VerificationOverview> AdminOverviewAsync(Guid provider,CancellationToken ct)
 {
  await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);var id=await Provider(c,tx,null,provider,ct);var data=await Read(c,tx,id,true,ct);await tx.CommitAsync(ct);return data.Overview;
 }
 public async Task<VerificationDocumentDetails> DetailsAsync(Guid? owner,Guid? provider,VerificationDocumentType type,bool admin,CancellationToken ct)
 {
  VerificationDocumentService.ValidateType(type);
  if(!admin&&owner is null)throw Missing();
  await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);var id=await Provider(c,tx,owner,provider,ct);var data=await Read(c,tx,id,admin,ct);await tx.CommitAsync(ct);
  return new(data.Overview.Documents.Single(d=>d.DocumentType==type),data.Files.Where(f=>f.Type==type).Select(f=>f.File).ToArray());
 }
 private static async Task Audit(MySqlConnection c,MySqlTransaction tx,Guid provider,Guid document,Guid actor,string kind,DateTime now,string? feedback,CancellationToken ct)
 {
  await using var cmd=Cmd(c,tx,"INSERT INTO provider_document_review_history(id,provider_id,document_id,event_type,actor_user_id,occurred_at,feedback)VALUES(@event,@provider,@doc,@kind,@actor,@now,@feedback)",("@event",Guid.NewGuid().ToString()),("@provider",provider.ToString()),("@doc",document.ToString()),("@kind",kind),("@actor",actor.ToString()),("@now",now),("@feedback",feedback));await cmd.ExecuteNonQueryAsync(ct);
 }
 public async Task UploadAsync(Guid owner,VerificationDocumentType type,ProviderDocumentUpload input,byte[] hash,IdentityDocumentKind? identityKind,DateTime now,CancellationToken ct)
 {
  VerificationDocumentService.ValidateType(type);
  await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(ct);var provider=await Provider(c,tx,owner,null,ct,true);Guid? previous=null;
  await using(var current=Cmd(c,tx,"""
   SELECT r.document_id,r.status,r.content_hash,d.file_name,s.resubmission_reason,r.identity_kind FROM provider_verification_slots s
   JOIN provider_document_reviews r ON r.document_id=s.current_document_id
   JOIN provider_documents d ON d.id=r.document_id
   WHERE s.provider_id=@id AND s.document_type=@type
   """,("@id",provider.ToString()),("@type",type.ToString())))
  await using(var r=await current.ExecuteReaderAsync(ct))if(await r.ReadAsync(ct))
  {
   previous=r.GetGuid(0);
   if(r.GetString(1)!="ActionRequired"&&(r.GetString(1)!="Verified"||r.IsDBNull(4))&&((byte[])r[2]).AsSpan().SequenceEqual(hash)&&r.GetString(3)==input.FileName&&(r.IsDBNull(5)?null:r.GetString(5))==identityKind?.ToString())
   {await r.DisposeAsync();await tx.CommitAsync(ct);return;}
  }
  // Retain the existing Provider storage limit, including immutable file history.
  await using(var count=Cmd(c,tx,"SELECT COUNT(*) FROM provider_documents WHERE provider_id=@id",("@id",provider.ToString())))
   if(Convert.ToInt32(await count.ExecuteScalarAsync(ct))>=20)throw Conflict("A maximum of 20 stored documents is allowed, including previous versions.");
  var id=Guid.NewGuid();
  await using(var file=Cmd(c,tx,"INSERT INTO provider_documents(id,provider_id,kind,file_name,content_type,content,created_at)VALUES(@doc,@provider,@type,@name,@contentType,@content,@now)",("@doc",id.ToString()),("@provider",provider.ToString()),("@type",type.ToString()),("@name",input.FileName),("@contentType",input.ContentType),("@content",input.Content),("@now",now)))await file.ExecuteNonQueryAsync(ct);
  await using(var review=Cmd(c,tx,"INSERT INTO provider_document_reviews(document_id,provider_id,document_type,status,content_hash,identity_kind)VALUES(@doc,@provider,@type,'Draft',@hash,@identityKind)",("@doc",id.ToString()),("@provider",provider.ToString()),("@type",type.ToString()),("@hash",hash),("@identityKind",identityKind?.ToString())))await review.ExecuteNonQueryAsync(ct);
  await using(var slot=Cmd(c,tx,"INSERT INTO provider_verification_slots(provider_id,document_type,current_document_id)VALUES(@provider,@type,@doc) ON DUPLICATE KEY UPDATE current_document_id=@doc",("@provider",provider.ToString()),("@type",type.ToString()),("@doc",id.ToString())))await slot.ExecuteNonQueryAsync(ct);
  if(previous.HasValue)await Audit(c,tx,provider,previous.Value,owner,"Replaced",now,null,ct);
  await Audit(c,tx,provider,id,owner,"Uploaded",now,null,ct);await tx.CommitAsync(ct);
 }
 public async Task SubmitAsync(Guid owner,IReadOnlyList<Guid> documents,DateTime now,CancellationToken ct)
 {
  await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(ct);var provider=await Provider(c,tx,owner,null,ct,true);
  foreach(var id in documents)
  {
   string? status;string? invalidation;
   await using(var current=Cmd(c,tx,"SELECT r.status,s.resubmission_reason FROM provider_verification_slots s JOIN provider_document_reviews r ON r.document_id=s.current_document_id AND r.provider_id=s.provider_id AND r.document_type=s.document_type WHERE s.provider_id=@provider AND r.document_id=@doc",("@provider",provider.ToString()),("@doc",id.ToString())))
   await using(var r=await current.ExecuteReaderAsync(ct))
   {if(await r.ReadAsync(ct)){status=r.GetString(0);invalidation=r.IsDBNull(1)?null:r.GetString(1);}else{status=null;invalidation=null;}}
   if(status is null)throw Conflict("The selected document is not current or does not belong to your profile. Reload before submitting.");
   if(status=="ActionRequired"||(status=="Verified"&&invalidation is not null))throw Conflict("Upload a replacement document before submitting it again.");
   if(status!="Draft")continue;
   await using(var cmd=Cmd(c,tx,"UPDATE provider_document_reviews SET status='InReview',submitted_at=@now WHERE document_id=@doc AND provider_id=@provider",("@now",now),("@doc",id.ToString()),("@provider",provider.ToString())))await cmd.ExecuteNonQueryAsync(ct);
   await Audit(c,tx,provider,id,owner,"Submitted",now,null,ct);
  }
  await tx.CommitAsync(ct);
 }
 public async Task ReviewAsync(Guid admin,Guid provider,VerificationDocumentType type,ReviewVerificationDocument input,DateTime now,CancellationToken ct)
 {
  await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(ct);await Provider(c,tx,null,provider,ct,true);
  string status;string? feedback;string? invalidation;
  await using(var cmd=Cmd(c,tx,"SELECT r.status,r.review_feedback,s.resubmission_reason FROM provider_verification_slots s JOIN provider_document_reviews r ON r.document_id=s.current_document_id AND r.provider_id=s.provider_id AND r.document_type=s.document_type WHERE s.provider_id=@provider AND s.document_type=@type AND r.document_id=@doc",("@provider",provider.ToString()),("@type",type.ToString()),("@doc",input.DocumentId.ToString())))
  await using(var r=await cmd.ExecuteReaderAsync(ct))
  {if(!await r.ReadAsync(ct))throw Conflict("The document changed. Reload and review the current file.");status=r.GetString(0);feedback=r.IsDBNull(1)?null:r.GetString(1);invalidation=r.IsDBNull(2)?null:r.GetString(2);}
  if(status=="Verified"&&invalidation is not null)throw Conflict("Identity details changed. Review a newly submitted identity document.");
  var target=input.Approved?"Verified":"ActionRequired";
  if(status==target&&feedback==input.Feedback){await tx.CommitAsync(ct);return;}
  if(status!="InReview")throw Conflict("Only a submitted document can be reviewed.");
  await using(var cmd=Cmd(c,tx,"UPDATE provider_document_reviews SET status=@status,reviewed_at=@now,reviewed_by=@admin,review_feedback=@feedback WHERE document_id=@doc",("@status",target),("@now",now),("@admin",admin.ToString()),("@feedback",input.Feedback),("@doc",input.DocumentId.ToString())))await cmd.ExecuteNonQueryAsync(ct);
  if(input.Approved)
  {await using var cmd=Cmd(c,tx,"UPDATE provider_verification_slots SET verified_document_id=@doc,resubmission_reason=NULL WHERE provider_id=@provider AND document_type=@type",("@doc",input.DocumentId.ToString()),("@provider",provider.ToString()),("@type",type.ToString()));await cmd.ExecuteNonQueryAsync(ct);}
  // Cache the derived overall state for legacy readers, never as the source of authorization.
  await using(var cmd=Cmd(c,tx,"UPDATE providers p SET is_verified=("+ProviderDocumentEligibility.Predicate+") WHERE p.id=@id",("@id",provider.ToString())))await cmd.ExecuteNonQueryAsync(ct);
  await Audit(c,tx,provider,input.DocumentId,admin,target,now,input.Feedback,ct);await tx.CommitAsync(ct);
 }
 public async Task<PendingVerificationPage> PendingAsync(int page,int size,CancellationToken ct)
 {
  await using var c=await Open(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
  const string source=" FROM provider_verification_slots s JOIN provider_document_reviews r ON r.document_id=s.current_document_id AND r.provider_id=s.provider_id AND r.document_type=s.document_type JOIN providers p ON p.id=s.provider_id JOIN provider_documents d ON d.id=r.document_id WHERE r.status='InReview'";
  await using var count=Cmd(c,tx,"SELECT COUNT(*)"+source);var total=Convert.ToInt64(await count.ExecuteScalarAsync(ct));
  await using var cmd=Cmd(c,tx,"SELECT r.*,p.display_name,d.file_name,d.content_type,d.created_at"+source+" ORDER BY r.submitted_at,r.document_id LIMIT @size OFFSET @offset",("@size",size),("@offset",(long)(page-1)*size));
  var items=new List<PendingVerificationDocument>();
  await using(var r=await cmd.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct)){var provider=r.GetGuid(r.GetOrdinal("provider_id"));items.Add(new(provider,r.GetString("display_name"),Enum.Parse<VerificationDocumentType>(r.GetString("document_type")),File(r,provider,true)));}
  await tx.CommitAsync(ct);return new(items,page,size,total);
 }
 public async Task<IReadOnlyList<DocumentReviewAudit>> AuditAsync(Guid provider,CancellationToken ct)
 {
  await using var c=await Open(ct);await Provider(c,null,null,provider,ct);
  await using var cmd=Cmd(c,null,"SELECT id,document_id,event_type,actor_user_id,occurred_at,feedback FROM provider_document_review_history WHERE provider_id=@id ORDER BY occurred_at,id",("@id",provider.ToString()));
  var list=new List<DocumentReviewAudit>();await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))list.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetGuid(3),DateTime.SpecifyKind(r.GetDateTime(4),DateTimeKind.Utc),r.IsDBNull(5)?null:r.GetString(5)));return list;
 }
}
