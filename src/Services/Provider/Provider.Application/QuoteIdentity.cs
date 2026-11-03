namespace Zumaris.Provider.Application;
public sealed record ProviderQuoteProfile(Guid ProviderId,Guid UserId,string DisplayName,string? PhotoUrl,decimal? Rating,int? CompletedJobs,bool IsVerified,int CategoryId);
public sealed record ProviderPublicSummary(Guid ProviderId,string DisplayName);
public interface IProviderQuoteRepository
{
    Task<ProviderPublicSummary?> SummaryAsync(Guid providerId,CancellationToken ct);
    Task<ProviderQuoteProfile?> GetAsync(Guid? userId,Guid? providerId,int categoryId,CancellationToken ct);
}
public interface IIdentitySessionValidator
{
    Task<bool> IsActiveAsync(string accessToken,bool originalRequestIsHttps,CancellationToken ct);
}
