namespace Zumaris.PropertyIntelligence.Domain;

public static class HealthCalculator
{
    public const string MethodVersion = "1.0";
    public static readonly IReadOnlyDictionary<string, decimal> Weights = new Dictionary<string, decimal>
    { ["Safety"] = .35m, ["Equipment"] = .30m, ["Maintenance"] = .25m, ["Issues"] = .10m }.AsReadOnly();
    public static int WeightedScore(decimal safety, decimal equipment, decimal maintenance, decimal issues) =>
        (int)decimal.Round(safety * .35m + equipment * .30m + maintenance * .25m + issues * .10m, 0, MidpointRounding.AwayFromZero);

    public static HealthScoreResult Calculate(Guid property, AssessmentRecord? record, IReadOnlyList<EquipmentEvidence> equipment, IReadOnlyList<DocumentEvidence> documents, DateTime now)
    {
        now = DateTime.SpecifyKind(now, DateTimeKind.Utc);
        var today = DateOnly.FromDateTime(now);
        var input = record?.Assessment ?? new HealthAssessment();
        var actions = new List<HealthAction>(); var alerts = new List<HealthAction>();
        var components = new List<HealthComponent>();
        void Action(string code, string message, Guid? id = null) => actions.Add(new(code, message, id));
        void Alert(string code, string message, Guid? id = null) => alerts.Add(new(code, message, id));
        void Component(string code, decimal? score, decimal coverage)
        {
            components.Add(new(code, Weights[code], score.HasValue ? decimal.Round(score.Value, 2) : null, decimal.Round(coverage, 2), score * Weights[code]));
        }
        if (record is null) Action("ASSESSMENT_REQUIRED", "Complete the property's self-reported health assessment.");
        var checks = input.SafetyChecks ?? [];
        bool inventoryComplete = input.SafetyInventoryComplete && HealthAssessmentRules.SafetyCodes.All(code => checks.Count(x => x.Code == code) == 1);
        var safetyScores = new List<decimal?>();
        foreach (var check in checks)
        {
            if (check.Applicability == "NotApplicable") continue;
            bool critical = check.Code is "ElectricalSafety" or "GasSafety";
            var doc = documents.FirstOrDefault(x => x.DocumentId == check.DocumentId);
            bool acceptedType = doc is not null && (check.Code == "Insurance" ? doc.Type == "Insurance" : doc.Type is "Certificate" or "Report");
            bool expired = acceptedType && doc?.ExpiresOn < today;
            if (critical && (check.Review == "Unsatisfactory" || expired)) Alert("SAFETY_RISK", "An electrical or gas safety check is unsatisfactory or its document has expired.", check.DocumentId);
            if (doc is null || !acceptedType)
            {
                safetyScores.Add(null); Action("SAFETY_DOCUMENT_REQUIRED", "Link an active document of the appropriate type for " + check.Code + ".", check.DocumentId); continue;
            }
            if ((doc.UpdatedAtUtc ?? doc.UploadedAtUtc) > record!.UpdatedAtUtc)
            {
                safetyScores.Add(null); Action("SAFETY_REVIEW_STALE", "Review the safety assessment again because the linked document changed.", doc.DocumentId); continue;
            }
            if (expired || check.Review == "Unsatisfactory")
            {
                safetyScores.Add(0); Action(expired ? "SAFETY_DOCUMENT_EXPIRED" : "SAFETY_UNSATISFACTORY", expired ? "Replace the expired safety document." : "Address the unsatisfactory safety check.", doc.DocumentId); continue;
            }
            if (check.Review != "Satisfactory" || doc.ExpiresOn is null && !check.HasNoExpiry)
            {
                safetyScores.Add(null); Action("SAFETY_REVIEW_REQUIRED", "Record the safety review and document expiry, or explicitly declare that it has no expiry.", doc.DocumentId); continue;
            }
            safetyScores.Add(100);
        }
        decimal safetyCoverage = inventoryComplete ? (safetyScores.Count == 0 ? 100 : 100m * safetyScores.Count(x => x.HasValue) / safetyScores.Count) : 0;
        decimal? safety = inventoryComplete && safetyCoverage == 100 ? (safetyScores.Count == 0 ? 100 : safetyScores.Average(x => x!.Value)) : null;
        if (!inventoryComplete) Action("SAFETY_INVENTORY_INCOMPLETE", "Confirm applicability for all safety checks and mark the safety inventory complete.");
        Component("Safety", safety, safetyCoverage);

        int Importance(EquipmentEvidence e) => e.Category is "Heating" or "Electrical" or "Safety" ? 3 : e.Category == "Plumbing" ? 2 : 1;
        decimal? EquipmentScore(EquipmentEvidence e) => e.Status switch { "Active" => 100m, "NeedsService" => 50m, "OutOfService" => 0m, _ => null };
        decimal weight = equipment.Sum(e => Importance(e));
        bool equipmentComplete = input.EquipmentInventoryComplete && equipment.Count > 0;
        decimal equipmentCoverage = equipmentComplete ? 100m * equipment.Where(e => EquipmentScore(e).HasValue).Sum(e => Importance(e)) / weight : 0;
        decimal? equipmentScore = equipmentComplete && equipmentCoverage == 100 ? equipment.Sum(e => EquipmentScore(e)!.Value * Importance(e)) / weight : null;
        foreach (var e in equipment)
        {
            if (e.Status != "Active") Action("EQUIPMENT_ATTENTION_REQUIRED", "Review the condition of " + e.Name + ".", e.EquipmentId);
            if (e.Status == "OutOfService" && e.Category is "Heating" or "Electrical" or "Safety") Alert("CRITICAL_EQUIPMENT_OUT_OF_SERVICE", "A high-importance equipment item is out of service.", e.EquipmentId);
        }
        if (!equipmentComplete) Action("EQUIPMENT_INVENTORY_INCOMPLETE", "Confirm the equipment inventory and record at least one active, nonarchived equipment item.");
        Component("Equipment", equipmentScore, equipmentCoverage);

        var exemptions = input.ServiceNotRequiredEquipmentIds ?? [];
        decimal? MaintenanceScore(EquipmentEvidence e)
        {
            // A recorded service deadline takes precedence over a servicing exemption.
            if (e.LastServicedOn > today || e.LastServicedOn > e.NextServiceOn) return null;
            if (e.NextServiceOn is null) return exemptions.Contains(e.EquipmentId) ? 100 : null;
            int days = today.DayNumber - e.NextServiceOn.Value.DayNumber;
            return days <= 0 ? 100 : days <= 30 ? 75 : days <= 90 ? 40 : 0;
        }
        decimal maintenanceCoverage = equipmentComplete ? 100m * equipment.Where(e => MaintenanceScore(e).HasValue).Sum(e => Importance(e)) / weight : 0;
        decimal? maintenance = equipmentComplete && maintenanceCoverage == 100 ? equipment.Sum(e => MaintenanceScore(e)!.Value * Importance(e)) / weight : null;
        foreach (var e in equipment)
        {
            if (MaintenanceScore(e) is null) Action("SERVICE_DATA_REQUIRED", "Record a valid service deadline or explicitly mark servicing as not required.", e.EquipmentId);
            else if (e.NextServiceOn < today) Action("SERVICE_OVERDUE", "Schedule the overdue service for " + e.Name + ".", e.EquipmentId);
        }
        Component("Maintenance", maintenance, maintenanceCoverage);

        var openIssues = (input.Issues ?? []).Where(x => x.ResolvedOn is null).ToArray();
        decimal penalty = 0;
        foreach (var issue in openIssues)
        {
            int initial = issue.Severity switch { "Low" => 10, "Medium" => 20, "High" => 30, "Critical" => 60, _ => 100 };
            int agePenalty = Math.Min(30, Math.Max(0, (today.DayNumber - issue.OpenedOn.DayNumber) / 30) * 5);
            penalty += initial + agePenalty;
            Action("OPEN_ISSUE", "Resolve the declared issue: " + issue.Title, issue.IssueId);
            if (issue.Severity == "Critical") Alert("CRITICAL_OPEN_ISSUE", "A critical self-reported issue remains unresolved.", issue.IssueId);
        }
        if (!input.IssueInventoryComplete) Action("ISSUE_INVENTORY_INCOMPLETE", "Explicitly confirm the inventory of known issues, including when no issues are known.");
        Component("Issues", input.IssueInventoryComplete ? Math.Max(0, 100 - penalty) : null, input.IssueInventoryComplete ? 100 : 0);

        bool ready = components.All(x => x.Score.HasValue);
        decimal coverage = decimal.Round(components.Sum(x => x.Weight * x.DataCoveragePercent), 2);
        if (!ready) coverage = Math.Min(99.99m, coverage);
        return new(property, ready ? WeightedScore(safety!.Value, equipmentScore!.Value, maintenance!.Value, Math.Max(0, 100 - penalty)) : null,
            ready ? "Calculated" : "InsufficientData", coverage, now, MethodVersion, "SelfReported", record?.UpdatedAtUtc,
            components, alerts.Distinct().ToArray(), actions.Distinct().ToArray());
    }
}
