namespace Zumaris.Provider.Application;
public sealed record ProviderLoginAccess(Guid UserId, Guid ProviderId, bool IsActive, bool HasSignedUp);
public interface IProviderLoginAccessRepository
{
    Task<ProviderLoginAccess?> GetAsync(Guid userId, CancellationToken ct);
}
