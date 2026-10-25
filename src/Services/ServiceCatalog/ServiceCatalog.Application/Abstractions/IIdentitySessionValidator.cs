namespace Zumaris.ServiceCatalog.Application.Abstractions;
public interface IIdentitySessionValidator { Task<bool> IsActiveAsync(string accessToken, bool originalRequestIsHttps, CancellationToken cancellationToken); }
