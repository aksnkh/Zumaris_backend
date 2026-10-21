using System.Text.Json.Serialization;
using Zumaris.Property.Domain.Entities;

namespace Zumaris.Property.Application.Features;

public sealed record CreateProperty(string AddressLine, string? Postcode, string City, string CountryCode,
    decimal? Latitude, decimal? Longitude, [property: JsonRequired] PropertyType PropertyType, [property: JsonRequired] int Bedrooms, [property: JsonRequired] int Bathrooms,
    int? YearBuilt, [property: JsonRequired] OwnershipRole OwnershipRole);
public sealed record UpdateProperty(string AddressLine, string? Postcode, string City, string CountryCode,
    decimal? Latitude, decimal? Longitude, [property: JsonRequired] PropertyType PropertyType, [property: JsonRequired] int Bedrooms, [property: JsonRequired] int Bathrooms,
    int? YearBuilt, [property: JsonRequired] OwnershipRole OwnershipRole, int Version)
{
    public CreateProperty AsCreate() => new(AddressLine, Postcode, City, CountryCode, Latitude, Longitude,
        PropertyType, Bedrooms, Bathrooms, YearBuilt, OwnershipRole);
}
public sealed record PropertyDto(Guid PropertyId, string AddressLine, string? Postcode, string City,
    string CountryCode, decimal? Latitude, decimal? Longitude, PropertyType PropertyType,
    int Bedrooms, int Bathrooms, int? YearBuilt, OwnershipRole OwnershipRole, bool IsCurrent,
    int? HealthScore, int? EquipmentCount, int? DocumentCount, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, int Version);
public sealed record PropertyPage(IReadOnlyList<PropertyDto> Items, long TotalCount, int PageNumber, int PageSize)
{
    public long TotalPages => TotalCount / PageSize + (TotalCount % PageSize == 0 ? 0 : 1);
}
public sealed record PropertyOption(string Value, string Label);
public sealed record CountryOption(string Code, string Name);
public sealed record PropertyOptions(IReadOnlyList<PropertyOption> PropertyTypes,
    IReadOnlyList<PropertyOption> OwnershipRoles, IReadOnlyList<CountryOption> Countries);
public sealed class PropertyValidationException(Dictionary<string, string[]> errors) : Exception("Invalid property input.")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
}
public sealed class PropertyNotFoundException() : Exception("Property not found.");
public sealed class PropertyForbiddenException() : Exception("Only the creator can modify or archive this property.");
public sealed class PropertyConflictException() : Exception("Property has changed. Reload before retrying.");
