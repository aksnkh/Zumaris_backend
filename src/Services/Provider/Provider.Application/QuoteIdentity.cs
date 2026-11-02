namespace Zumaris.Provider.Application;
public sealed record ProviderQuoteProfile(Guid ProviderId,Guid UserId,string DisplayName,string? PhotoUrl,decimal? Rating,int? CompletedJobs,bool IsVerified,int CategoryId);
public interface IProviderQuoteRepository
{
    Task<ProviderQuoteProfile?> GetAsync(Guid? userId,Guid? providerId,int categoryId,CancellationToken ct);
}
public interface IIdentitySessionValidator
{
    Task<bool> IsActiveAsync(string accessToken,bool originalRequestIsHttps,CancellationToken ct);
}
