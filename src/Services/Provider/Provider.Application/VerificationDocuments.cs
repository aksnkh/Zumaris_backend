using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
namespace Zumaris.Provider.Application;

public enum VerificationDocumentType { IdentityCheck=1,Insurance,Certificate }
public enum IdentityDocumentKind { Passport=1,DrivingLicence,OtherId }
public sealed record VerificationProfile(string? Postcode,string? OtherSkills);
public sealed record SaveVerificationProfile([Required,StringLength(20,MinimumLength=2)] string Postcode,[StringLength(1000)] string? OtherSkills);
public sealed record VerificationFile(Guid DocumentId,string FileName,string ContentType,DateTime UploadedAtUtc,string Status,
 DateTime? SubmittedAtUtc,DateTime? ReviewedAtUtc,string? ReviewFeedback,string DownloadPath,IdentityDocumentKind? IdentityKind=null);
public sealed record VerificationDocumentCard(VerificationDocumentType DocumentType,string DisplayName,string Status,
 bool RequiredForOrders,bool IsVerified,string? Feedback,VerificationFile? CurrentDocument,VerificationFile? VerifiedDocument);
public sealed record VerificationOverview(IReadOnlyList<VerificationDocumentCard> Documents,VerificationProfile? Profile=null);
public sealed record VerificationDocumentDetails(VerificationDocumentCard Document,IReadOnlyList<VerificationFile> History);
public sealed record SubmitVerificationDocuments([Required,MinLength(1),MaxLength(3)] Guid[] DocumentIds);
public sealed record SubmitVerificationDocument(Guid DocumentId);
public sealed record ReviewVerificationDocument(Guid DocumentId,bool Approved,[StringLength(1000)] string? Feedback);
public sealed record PendingVerificationDocument(Guid ProviderId,string ProviderName,VerificationDocumentType DocumentType,VerificationFile Document);
public sealed record PendingVerificationPage(IReadOnlyList<PendingVerificationDocument> Items,int PageNumber,int PageSize,long TotalCount);
public sealed record DocumentReviewAudit(Guid Id,Guid DocumentId,string EventType,Guid ActorUserId,DateTime OccurredAtUtc,string? Feedback);
public interface IProviderVerificationRepository
{
 Task<VerificationProfile> SaveProfileAsync(Guid owner,SaveVerificationProfile input,CancellationToken ct);
 Task<VerificationOverview> OverviewAsync(Guid owner,CancellationToken ct);
 Task<VerificationDocumentDetails> DetailsAsync(Guid? owner,Guid? provider,VerificationDocumentType type,bool admin,CancellationToken ct);
 Task UploadAsync(Guid owner,VerificationDocumentType type,ProviderDocumentUpload input,byte[] hash,IdentityDocumentKind? identityKind,DateTime now,CancellationToken ct);
 Task SubmitAsync(Guid owner,IReadOnlyList<Guid> documents,DateTime now,CancellationToken ct);
 Task<VerificationOverview> AdminOverviewAsync(Guid provider,CancellationToken ct);
 Task ReviewAsync(Guid admin,Guid provider,VerificationDocumentType type,ReviewVerificationDocument input,DateTime now,CancellationToken ct);
 Task<PendingVerificationPage> PendingAsync(int page,int size,CancellationToken ct);
 Task<IReadOnlyList<DocumentReviewAudit>> AuditAsync(Guid provider,CancellationToken ct);
}
public sealed class VerificationDocumentService(IProviderVerificationRepository repository,TimeProvider clock)
{
 public const int MaxFileBytes=5*1024*1024;
 public static void ValidateType(VerificationDocumentType type)
 {if(!Enum.IsDefined(type))throw new ProviderWorkflowException(400,"Select a valid verification document type.");}
 public async Task<VerificationDocumentDetails> UploadAsync(Guid owner,VerificationDocumentType type,ProviderDocumentUpload input,CancellationToken ct,IdentityDocumentKind? identityKind=null)
 {
  ValidateType(type);
  if(type==VerificationDocumentType.IdentityCheck ? identityKind is null||!Enum.IsDefined(identityKind.Value) : identityKind is not null)throw new ProviderWorkflowException(400,"Select Passport, DrivingLicence or OtherId for Identity Check only.");
  if(input.Content.Length is <1 or >MaxFileBytes)throw new ProviderWorkflowException(400,"Each PDF, JPEG or PNG document must be 1 byte to 5 MiB.");
  var name=Path.GetFileName(input.FileName.Replace('\\','/'));
  if(string.IsNullOrWhiteSpace(name)||name.Length>200||name.Any(char.IsControl))throw new ProviderWorkflowException(400,"The document filename is invalid.");
  var bytes=input.Content;
  string contentType=bytes.AsSpan().StartsWith("%PDF-"u8)?"application/pdf":bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10})?"image/png":bytes.Length>=3&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255?"image/jpeg":"";
  if(contentType.Length==0)throw new ProviderWorkflowException(400,"Only PDF, JPEG and PNG documents are accepted.");
  await repository.UploadAsync(owner,type,input with{FileName=name,ContentType=contentType},SHA256.HashData(bytes),identityKind,clock.GetUtcNow().UtcDateTime,ct);
  return await repository.DetailsAsync(owner,null,type,false,ct);
 }
 public async Task<VerificationOverview> SubmitAsync(Guid owner,IReadOnlyList<Guid>? documents,CancellationToken ct)
 {
  if(documents is null||documents.Count is <1 or >3||documents.Any(id=>id==Guid.Empty)||documents.Distinct().Count()!=documents.Count)
   throw new ProviderWorkflowException(400,"Provide 1 to 3 distinct document IDs.");
  await repository.SubmitAsync(owner,documents,clock.GetUtcNow().UtcDateTime,ct);return await repository.OverviewAsync(owner,ct);
 }
 public async Task<VerificationDocumentDetails> ReviewAsync(Guid admin,Guid provider,VerificationDocumentType type,ReviewVerificationDocument input,CancellationToken ct)
 {
  ValidateType(type);
  if(input.DocumentId==Guid.Empty)throw new ProviderWorkflowException(400,"A document ID is required.");
  if(!input.Approved&&string.IsNullOrWhiteSpace(input.Feedback))throw new ProviderWorkflowException(400,"Provide feedback when requesting document changes.");
  if(input.Feedback?.Length>1000)throw new ProviderWorkflowException(400,"Review feedback must not exceed 1000 characters.");
  await repository.ReviewAsync(admin,provider,type,input with{Feedback=string.IsNullOrWhiteSpace(input.Feedback)?null:input.Feedback.Trim()},clock.GetUtcNow().UtcDateTime,ct);
  return await repository.DetailsAsync(null,provider,type,true,ct);
 }
}
