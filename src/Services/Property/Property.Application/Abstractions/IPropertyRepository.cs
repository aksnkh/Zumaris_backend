using Zumaris.Property.Application.Features;
using Zumaris.Property.Domain.Entities;

namespace Zumaris.Property.Application.Abstractions;

public interface IPropertyRepository
{
    Task<PropertyAccess> CreateAsync(PropertyRecord property, OwnershipRole role, CancellationToken ct);
    Task<(IReadOnlyList<PropertyAccess> Items, long Total)> ListAsync(Guid userId, int pageNumber, int pageSize, CancellationToken ct);
    Task<PropertyAccess?> GetAsync(Guid userId, Guid propertyId, CancellationToken ct);
    Task<PropertyAccess?> GetCurrentAsync(Guid userId, CancellationToken ct);
    Task<PropertyAccess> UpdateAsync(Guid userId, Guid propertyId, CreateProperty input, int version, DateTime now, CancellationToken ct);
    Task ArchiveAsync(Guid userId, Guid propertyId, int version, DateTime now, CancellationToken ct);
    Task<PropertyAccess> SetCurrentAsync(Guid userId, Guid propertyId, CancellationToken ct);
}
public interface IIdentitySessionValidator
{
    Task<bool> IsActiveAsync(string accessToken, bool originalRequestIsHttps, CancellationToken ct);
}
