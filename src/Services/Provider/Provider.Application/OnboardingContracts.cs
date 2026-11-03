using System.ComponentModel.DataAnnotations;
namespace Zumaris.Provider.Application;
public sealed record SaveProviderRequest([Required, StringLength(200, MinimumLength=1)] string DisplayName,
    [Required, MinLength(1), MaxLength(100)] int[] CategoryIds);
public sealed record ProviderDocument(Guid Id, string Kind, string FileName, string ContentType, DateTime CreatedAtUtc);
public sealed record ProviderAccount(Guid ProviderId, Guid UserId, string DisplayName, bool IsActive, bool IsVerified,
    string OnboardingStatus, int Revision, string? ReviewReason, IReadOnlyList<int> CategoryIds, IReadOnlyList<ProviderDocument> Documents);
public sealed record ProviderEligibility(Guid ProviderId, Guid UserId, IReadOnlyList<int> CategoryIds);
public sealed record ReviewProviderRequest([Range(1,int.MaxValue)] int Revision, bool Approved, [StringLength(1000)] string? Reason);
public sealed record CustomerReviewRequest([Range(1,5)] int Rating, [Required, StringLength(2000,MinimumLength=1)] string Comment);
public sealed record CustomerReview(Guid RequestId, Guid ProviderId, int Rating, string Comment, DateTime CreatedAtUtc);
public sealed record CompletedWork(Guid RequestId, Guid CustomerId, Guid ProviderId, string Status, Guid QuoteId, decimal Amount, string CurrencyCode);
public sealed record ProviderDocumentUpload(string Kind,string FileName,string ContentType,byte[] Content);
public sealed record PrivateProviderDocument(string FileName,string ContentType,byte[] Content);
public sealed record ProviderPage(IReadOnlyList<ProviderAccount> Items,int PageNumber,int PageSize,long TotalCount);
public interface IProviderAccountRepository
{
 Task<ProviderAccount?> GetAsync(Guid userId,CancellationToken ct);
 Task<ProviderAccount> SaveAsync(Guid userId,SaveProviderRequest input,CancellationToken ct);
 Task<ProviderDocument> AddDocumentAsync(Guid userId,ProviderDocumentUpload input,CancellationToken ct);
 Task<PrivateProviderDocument?> DocumentAsync(Guid? owner,Guid providerId,Guid documentId,CancellationToken ct);
 Task<ProviderPage> PendingAsync(int page,int size,CancellationToken ct);
 Task<ProviderAccount?> ByIdAsync(Guid id,CancellationToken ct);
 Task<ProviderAccount> ReviewAsync(Guid admin,Guid providerId,ReviewProviderRequest input,CancellationToken ct);
 Task<CustomerReview> AddReviewAsync(Guid customer,CompletedWork work,CustomerReviewRequest input,CancellationToken ct);
 Task<IReadOnlyList<CustomerReview>> ReviewsAsync(Guid providerId,int page,int size,CancellationToken ct);
}
public interface IProviderWorkflowClient
{
 Task<bool> IsAdminAsync(string bearer,CancellationToken ct);
 Task<CompletedWork> CompletedWorkAsync(Guid requestId,string bearer,CancellationToken ct);
 Task<int?> CompletedJobsAsync(Guid providerId,string bearer,CancellationToken ct);
}
public sealed class ProviderWorkflowException(int status,string message):Exception(message) { public int Status {get;}=status; }
