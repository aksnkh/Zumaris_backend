using Zumaris.Property.Application.Abstractions;
using Zumaris.Property.Domain.Entities;

namespace Zumaris.Property.Application.Features;

public interface IPropertyService
{
    Task<PropertyDto> CreateAsync(Guid userId, CreateProperty input, CancellationToken ct);
    Task<PropertyPage> ListAsync(Guid userId, int pageNumber, int pageSize, CancellationToken ct);
    Task<PropertyDto> GetAsync(Guid userId, Guid propertyId, CancellationToken ct);
    Task<PropertyDto> GetCurrentAsync(Guid userId, CancellationToken ct);
    Task<PropertyDto> UpdateAsync(Guid userId, Guid propertyId, UpdateProperty input, CancellationToken ct);
    Task ArchiveAsync(Guid userId, Guid propertyId, int version, CancellationToken ct);
    Task<PropertyDto> SetCurrentAsync(Guid userId, Guid propertyId, CancellationToken ct);
}
public sealed class PropertyService(IPropertyRepository repository, TimeProvider clock) : IPropertyService
{
    public async Task<PropertyDto> CreateAsync(Guid userId, CreateProperty input, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var x = PropertyRules.Normalize(input, now.Year);
        var record = new PropertyRecord(Guid.NewGuid(),x.AddressLine,x.Postcode,x.City,x.CountryCode,x.Latitude,x.Longitude,
            x.PropertyType,x.Bedrooms,x.Bathrooms,x.YearBuilt,userId,now,null,true,1);
        return ToDto(await repository.CreateAsync(record,x.OwnershipRole,ct));
    }
    public async Task<PropertyPage> ListAsync(Guid userId, int pageNumber, int pageSize, CancellationToken ct)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 100)
            throw new PropertyValidationException(new() { ["pagination"] = ["Use positive pageNumber and pageSize between 1 and 100."] });
        var (items,total) = await repository.ListAsync(userId,pageNumber,pageSize,ct);
        return new(items.Select(ToDto).ToArray(),total,pageNumber,pageSize);
    }
    public async Task<PropertyDto> GetAsync(Guid userId, Guid propertyId, CancellationToken ct) =>
        ToDto(await repository.GetAsync(userId,propertyId,ct) ?? throw new PropertyNotFoundException());
    public async Task<PropertyDto> GetCurrentAsync(Guid userId, CancellationToken ct) =>
        ToDto(await repository.GetCurrentAsync(userId,ct) ?? throw new PropertyNotFoundException());
    public async Task<PropertyDto> UpdateAsync(Guid userId, Guid propertyId, UpdateProperty input, CancellationToken ct)
    {
        ValidateVersion(input.Version); var now=clock.GetUtcNow().UtcDateTime;
        return ToDto(await repository.UpdateAsync(userId,propertyId,PropertyRules.Normalize(input.AsCreate(),now.Year),input.Version,now,ct));
    }
    public Task ArchiveAsync(Guid userId, Guid propertyId, int version, CancellationToken ct)
    { ValidateVersion(version); return repository.ArchiveAsync(userId,propertyId,version,clock.GetUtcNow().UtcDateTime,ct); }
    public async Task<PropertyDto> SetCurrentAsync(Guid userId, Guid propertyId, CancellationToken ct) =>
        ToDto(await repository.SetCurrentAsync(userId,propertyId,ct));
    private static void ValidateVersion(int version)
    {
        if (version < 1) throw new PropertyValidationException(new() { ["version"] = ["Use the latest positive property version."] });
    }
    public static PropertyDto ToDto(PropertyAccess a)
    {
        var x=a.Property;
        return new(x.PropertyId,x.AddressLine,x.Postcode,x.City,x.CountryCode,x.Latitude,x.Longitude,x.PropertyType,x.Bedrooms,
            x.Bathrooms,x.YearBuilt,a.OwnershipRole,a.IsCurrent,null,null,null,x.CreatedAtUtc,x.UpdatedAtUtc,x.Version);
    }
}
