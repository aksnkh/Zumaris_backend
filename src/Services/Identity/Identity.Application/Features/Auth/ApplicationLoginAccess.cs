using Identity.Application.Abstractions.Persistence;
namespace Identity.Application.Features.Auth;

public sealed record ApplicationGrant(string Application, Guid? ProviderId);
public sealed record ProviderLoginAccess(Guid UserId, Guid ProviderId, bool IsActive, bool HasSignedUp);
public interface IProviderLoginAccessClient
{
    Task<ProviderLoginAccess?> GetAsync(Guid userId, CancellationToken ct);
}
public sealed class ApplicationLoginException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
public sealed class ApplicationLoginAccess(IProviderLoginAccessClient providers, IPlatformAdminRepository administrators, IApplicationAccountRepository accounts, IProviderEnrollmentClient enrollment)
{
    public async Task<ApplicationGrant> ResolveAsync(Guid userId, string? application, CancellationToken ct)
    {
        if (application is not ("App" or "Tech" or "Admin"))
            throw new ApplicationLoginException(400, "Application must be App, Tech or Admin.");
        var account=await accounts.FindByIdAsync(application,userId,ct);
        if(account is null||!account.IsActive)throw new ApplicationLoginException(403,"An active account in this application is required.");
        if (application == "App") return new(application, null);
        if (application == "Admin")
        {
            if (!await administrators.IsAdminAsync(userId, ct))
                throw new ApplicationLoginException(403, "Administrator access is required to sign in to Admin.");
            return new(application, null);
        }
        var provider = await providers.GetAsync(userId, ct);
        if(provider is null||!provider.HasSignedUp)
        {
            if(account.ProviderEnrollmentRequestedAtUtc is null)throw new ApplicationLoginException(403,"Complete Tech registration and consent before signing in.");
            provider=await enrollment.EnrollAsync(account,ct);
        }
        if (provider is null || !provider.IsActive || !provider.HasSignedUp)
            throw new ApplicationLoginException(403, "Complete provider signup with your existing account before signing in to Tech.");
        if (provider.UserId != userId || provider.ProviderId == Guid.Empty)
            throw new ApplicationLoginException(503, "Provider sign-in validation is temporarily unavailable.");
        return new(application, provider.ProviderId);
    }
}
