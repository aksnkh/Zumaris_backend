namespace Zumaris.PropertyIntelligence.Domain;

public static class HealthAssessmentRules
{
    public static readonly IReadOnlyList<string> SafetyCodes = Array.AsReadOnly(new[] { "ElectricalSafety", "GasSafety", "EnergyPerformance", "Insurance" });
    public static IReadOnlyList<string> Validate(HealthAssessment input, DateOnly today)
    {
        var errors = new List<string>();
        var checks = input.SafetyChecks ?? [];
        var issues = input.Issues ?? [];
        var exempt = input.ServiceNotRequiredEquipmentIds ?? [];
        if (checks.Count > 4 || checks.Any(x => x is null) || checks.Select(x => x?.Code).Distinct().Count() != checks.Count)
            errors.Add("Provide up to four distinct safety checks.");
        foreach (var c in checks.Where(x => x is not null))
        {
            if (!SafetyCodes.Contains(c.Code) || c.Applicability is not ("Required" or "NotApplicable") || c.Review is not ("Unknown" or "Satisfactory" or "Unsatisfactory"))
                errors.Add("Use the supported safety codes, applicability and review options.");
            if (c.DocumentId == Guid.Empty || c.NotApplicableReason?.Length > 500) errors.Add("Use a valid document ID and a reason of up to 500 characters.");
            if (c.Applicability == "NotApplicable" && (string.IsNullOrWhiteSpace(c.NotApplicableReason) || c.NotApplicableReason.Trim().Length < 10 || c.DocumentId.HasValue || c.HasNoExpiry || c.Review != "Unknown"))
                errors.Add("NotApplicable requires a reason of 10–500 characters and no document, expiry declaration or review result.");
        }
        if (input.SafetyInventoryComplete && (checks.Count != 4 || SafetyCodes.Any(code => !checks.Any(c => c is not null && c.Code == code))))
            errors.Add("Confirm applicability for all four safety checks before declaring the safety inventory complete.");
        if (exempt.Count > 500 || exempt.Contains(Guid.Empty) || exempt.Distinct().Count() != exempt.Count) errors.Add("Provide up to 500 distinct equipment IDs exempt from servicing.");
        if (issues.Count > 200 || issues.Any(x => x is null) || issues.Select(x => x?.IssueId).Distinct().Count() != issues.Count) errors.Add("Provide up to 200 distinct issues.");
        foreach (var i in issues.Where(x => x is not null))
        {
            if (i.IssueId == Guid.Empty || string.IsNullOrWhiteSpace(i.Title) || i.Title.Length > 300 || i.Severity is not ("Low" or "Medium" or "High" or "Critical")) errors.Add("Each issue requires an ID, title of 1–300 characters and supported severity.");
            if (i.OpenedOn.Year < 1900 || i.OpenedOn > today || i.ResolvedOn < i.OpenedOn || i.ResolvedOn > today) errors.Add("Issue dates must be chronological, no earlier than 1900 and not in the future.");
        }
        return errors.Distinct().ToArray();
    }
}
