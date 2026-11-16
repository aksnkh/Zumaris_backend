using Zumaris.PropertyIntelligence.Domain;
using Xunit;
namespace PropertyIntelligence.Tests;

public sealed class HealthCalculatorTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);
    private readonly Guid property = Guid.NewGuid();
    private readonly Guid equipmentId = Guid.NewGuid();
    private readonly Guid documentId = Guid.NewGuid();
    private EquipmentEvidence Equipment => new(equipmentId, "Boiler", "Heating", "Active", Today.AddDays(-100), Today.AddDays(100));
    private DocumentEvidence Document => new(documentId, "Certificate", Today.AddDays(100), Now.AddDays(-2), null);
    private HealthAssessment Complete => new(true, true, true, HealthAssessmentRules.SafetyCodes.Select(code => code == "ElectricalSafety"
        ? new SafetyCheck(code, "Required", "Satisfactory", documentId)
        : new SafetyCheck(code, "NotApplicable", NotApplicableReason: "Not applicable to this property's declared circumstances.")).ToArray());
    private HealthScoreResult Calculate(HealthAssessment? input = null, EquipmentEvidence[]? eq = null, DocumentEvidence[]? docs = null, DateTime? now = null) =>
        HealthCalculator.Calculate(property, new(property, input ?? Complete, Guid.NewGuid(), Now.AddDays(-1)), eq ?? [Equipment], docs ?? [Document], now ?? Now);
    private static HealthComponent Component(HealthScoreResult result, string code) => Assert.Single(result.Components, c => c.Code == code);

    [Fact]
    public void Approved_coefficients_produce_the_documented_example()
    {
        Assert.Equal(1m, HealthCalculator.Weights.Values.Sum());
        Assert.Equal(73, HealthCalculator.WeightedScore(80, 70, 60, 90));
    }
    [Fact]
    public void Complete_healthy_evidence_returns_100_with_self_reported_provenance()
    {
        var result = Calculate();
        Assert.Equal(100, result.HealthScore); Assert.Equal(100, result.DataCoveragePercent);
        Assert.Equal("Calculated", result.Status); Assert.Equal("SelfReported", result.AssessmentSource);
        Assert.Equal(Now, result.CalculatedAtUtc); Assert.Equal("1.0", result.MethodVersion);
        Assert.Empty(result.CriticalAlerts); Assert.Empty(result.ImprovementActions);
    }
    [Fact]
    public void No_assessment_is_unknown_even_when_equipment_and_documents_exist()
    {
        var result = HealthCalculator.Calculate(property, null, [Equipment], [Document], Now);
        Assert.Null(result.HealthScore); Assert.Equal(0, result.DataCoveragePercent);
        Assert.Equal("InsufficientData", result.Status);
    }
    [Fact]
    public void An_unlinked_document_or_missing_review_does_not_earn_safety_points()
    {
        var missing = Calculate(docs: []);
        Assert.Null(missing.HealthScore); Assert.Equal(65, missing.DataCoveragePercent);
        var checks = Complete.SafetyChecks!.Select(c => c.Code == "ElectricalSafety" ? c with { Review = "Unknown" } : c).ToArray();
        Assert.Null(Calculate(Complete with { SafetyChecks = checks }).HealthScore);
    }
    [Fact]
    public void Absent_issue_inventory_is_not_assumed_to_mean_no_issues()
    {
        var result = Calculate(Complete with { IssueInventoryComplete = false });
        Assert.Null(result.HealthScore); Assert.Null(Component(result, "Issues").Score);
        Assert.Equal(90, result.DataCoveragePercent);
    }
    [Fact]
    public void Expired_electrical_document_scores_zero_and_emits_a_separate_critical_alert()
    {
        var result = Calculate(docs: [Document with { ExpiresOn = Today.AddDays(-1) }]);
        Assert.Equal(65, result.HealthScore); Assert.Equal(0, Component(result, "Safety").Score);
        Assert.Contains(result.CriticalAlerts, x => x.Code == "SAFETY_RISK");
    }
    [Fact]
    public void Expiry_day_is_valid_but_the_next_day_is_recalculated_without_using_a_stale_score()
    {
        var documents = new[] { Document with { ExpiresOn = Today } };
        Assert.Equal(100, Calculate(docs: documents).HealthScore);
        Assert.Equal(65, Calculate(docs: documents, now: Now.AddDays(1)).HealthScore);
    }
    [Fact]
    public void A_document_change_requires_a_new_self_reported_review()
    {
        var result = Calculate(docs: [Document with { UpdatedAtUtc = Now }]);
        Assert.Null(result.HealthScore);
        Assert.Contains(result.ImprovementActions, x => x.Code == "SAFETY_REVIEW_STALE");
    }
    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 75)]
    [InlineData(30, 75)]
    [InlineData(31, 40)]
    [InlineData(90, 40)]
    [InlineData(91, 0)]
    public void Maintenance_penalties_follow_the_documented_overdue_boundaries(int days, int expected)
    {
        var result = Calculate(eq: [Equipment with { LastServicedOn = null, NextServiceOn = Today.AddDays(-days) }]);
        Assert.Equal(expected, Component(result, "Maintenance").Score);
    }
    [Fact]
    public void An_exemption_cannot_hide_a_recorded_overdue_service()
    {
        var result = Calculate(Complete with { ServiceNotRequiredEquipmentIds = [equipmentId] }, eq: [Equipment with { NextServiceOn = Today.AddDays(-100), LastServicedOn = null }]);
        Assert.Equal(0, Component(result, "Maintenance").Score);
    }
    [Fact]
    public void Missing_deadlines_are_unknown_until_explicitly_exempted()
    {
        var eq = new[] { Equipment with { NextServiceOn = null } };
        Assert.Null(Calculate(eq: eq).HealthScore);
        Assert.Equal(100, Calculate(Complete with { ServiceNotRequiredEquipmentIds = [equipmentId] }, eq: eq).HealthScore);
    }
    [Fact]
    public void Future_service_completion_is_unknown_rather_than_a_reward()
    {
        Assert.Null(Calculate(eq: [Equipment with { LastServicedOn = Today.AddDays(1) }]).HealthScore);
    }
    [Fact]
    public void Equipment_importance_is_weighted_and_critical_warnings_survive_the_average()
    {
        var result = Calculate(eq: [Equipment with { Status = "OutOfService" }, Equipment with { EquipmentId = Guid.NewGuid(), Category = "Appliance" }]);
        Assert.Equal(25, Component(result, "Equipment").Score); Assert.Equal(78, result.HealthScore);
        Assert.Contains(result.CriticalAlerts, x => x.Code == "CRITICAL_EQUIPMENT_OUT_OF_SERVICE");
    }
    [Fact]
    public void Unknown_equipment_status_does_not_default_to_healthy()
    {
        Assert.Null(Calculate(eq: [Equipment with { Status = "Unknown" }]).HealthScore);
        Assert.Null(Calculate(eq: []).HealthScore);
    }
    [Fact]
    public void Issue_age_adds_penalty_and_resolving_it_removes_the_penalty()
    {
        var issue = new DeclaredIssue(Guid.NewGuid(), "Leaking tap", "Medium", Today.AddDays(-30));
        Assert.Equal(75, Component(Calculate(Complete with { Issues = [issue] }), "Issues").Score);
        Assert.Equal(100, Calculate(Complete with { Issues = [issue with { ResolvedOn = Today }] }).HealthScore);
    }
    [Fact]
    public void Critical_issues_are_visible_even_without_sufficient_data_and_penalty_is_bounded()
    {
        var issues = Enumerable.Range(0, 5).Select(i => new DeclaredIssue(Guid.NewGuid(), "Critical defect", "Critical", Today.AddDays(-300))).ToArray();
        var result = Calculate(Complete with { SafetyInventoryComplete = false, Issues = issues });
        Assert.Null(result.HealthScore); Assert.Equal(0, Component(result, "Issues").Score);
        Assert.NotEmpty(result.CriticalAlerts);
    }
    [Fact]
    public void Validation_rejects_unsupported_options_duplicate_ids_and_future_issue_dates()
    {
        var invalid = new HealthAssessment(true, true, true,
            [new("ElectricalSafety", "NotApplicable", NotApplicableReason: "No")],
            [Guid.Empty, Guid.Empty], [new(Guid.Empty, "", "Invalid", Today.AddDays(1))]);
        Assert.NotEmpty(HealthAssessmentRules.Validate(invalid, Today));
        Assert.Empty(HealthAssessmentRules.Validate(Complete, Today));
    }
}
