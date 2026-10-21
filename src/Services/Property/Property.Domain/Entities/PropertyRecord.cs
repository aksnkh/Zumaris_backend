namespace Zumaris.Property.Domain.Entities;

public enum PropertyType { Terraced, SemiDetached, Detached, FlatApartment, Bungalow, Other }
public enum OwnershipRole { Owner, Tenant, Landlord, PropertyManager }

public sealed record PropertyRecord(Guid PropertyId, string AddressLine, string? Postcode, string City,
    string CountryCode, decimal? Latitude, decimal? Longitude, PropertyType PropertyType,
    int Bedrooms, int Bathrooms, int? YearBuilt, Guid CreatedByUserId, DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc, bool IsActive, int Version);
public sealed record PropertyAccess(PropertyRecord Property, OwnershipRole OwnershipRole, bool IsCurrent);
