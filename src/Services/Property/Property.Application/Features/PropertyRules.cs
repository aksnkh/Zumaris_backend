using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Zumaris.Property.Application.Features;

public static class PropertyRules
{
    public static IReadOnlyList<CountryOption> Countries { get; } = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(c => new RegionInfo(c.Name)).Where(r => r.TwoLetterISORegionName.Length == 2 && r.TwoLetterISORegionName != "IV")
        .GroupBy(r => r.TwoLetterISORegionName).Select(g => new CountryOption(g.Key, g.First().EnglishName))
        .OrderBy(c => c.Name, StringComparer.Ordinal).ToArray();
    public static CreateProperty Normalize(CreateProperty input, int currentYear)
    {
        var errors = new Dictionary<string, string[]>();
        void Error(string field, string message) => errors[field] = [message];
        var address = input.AddressLine?.Trim() ?? "";
        var city = input.City?.Trim() ?? "";
        var country = input.CountryCode?.Trim().ToUpperInvariant() ?? "";
        var postcode = string.IsNullOrWhiteSpace(input.Postcode) ? null : input.Postcode.Trim();
        if (country == "GB") postcode = postcode?.ToUpperInvariant();
        if (address.EnumerateRunes().Count() is < 3 or > 300) Error("addressLine", "Use 3–300 characters.");
        if (city.EnumerateRunes().Count() is < 1 or > 100) Error("city", "Use 1–100 characters.");
        if (!Countries.Any(c => c.Code == country)) Error("countryCode", "Choose a country code from property-options (GB for United Kingdom).");
        if (postcode?.EnumerateRunes().Count() > 20) Error("postcode", "Use at most 20 characters.");
        if (country == "GB" && (postcode is null || postcode.Length > 20 || !Regex.IsMatch(postcode,
            @"^(GIR ?0AA|(?:[A-PR-UWYZ][0-9][0-9A-HJKPSTUW]?|[A-PR-UWYZ][A-HK-Y][0-9][0-9ABEHMNPRVWXY]?) ?[0-9][ABD-HJLNP-UW-Z]{2})$",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))) Error("postcode", "Provide a valid UK postcode.");
        if (!Enum.IsDefined(input.PropertyType)) Error("propertyType", "Choose a supported property type.");
        if (!Enum.IsDefined(input.OwnershipRole)) Error("ownershipRole", "Choose Owner, Tenant, Landlord or PropertyManager.");
        if (input.Bedrooms is < 0 or > 100) Error("bedrooms", "Use 0–100; a studio may have zero bedrooms.");
        if (input.Bathrooms is < 0 or > 100) Error("bathrooms", "Use 0–100.");
        if (input.YearBuilt.HasValue && (input.YearBuilt < 1 || input.YearBuilt > currentYear)) Error("yearBuilt", "Use a valid year no later than the current year, or null.");
        if (input.Latitude.HasValue != input.Longitude.HasValue) Error("coordinates", "Provide both latitude and longitude, or neither.");
        if (input.Latitude is < -90 or > 90 || input.Longitude is < -180 or > 180) Error("coordinates", "Coordinates are outside their valid ranges.");
        if ((input.Latitude.HasValue && decimal.Round(input.Latitude.Value,7) != input.Latitude) ||
            (input.Longitude.HasValue && decimal.Round(input.Longitude.Value,7) != input.Longitude)) Error("coordinates", "Use at most seven decimal places.");
        if (errors.Count > 0) throw new PropertyValidationException(errors);
        return input with { AddressLine = address, City = city, CountryCode = country, Postcode = postcode };
    }
}
