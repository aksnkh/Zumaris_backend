namespace Zumaris.PropertyIntelligence.Domain;

public sealed record SafetyCheck(string Code, string Applicability, string Review = "Unknown", Guid? DocumentId = null, bool HasNoExpiry = false, string? NotApplicableReason = null);
public sealed record DeclaredIssue(Guid IssueId, string Title, string Severity, DateOnly OpenedOn, DateOnly? ResolvedOn = null);
public sealed record HealthAssessment(bool SafetyInventoryComplete = false, bool EquipmentInventoryComplete = false, bool IssueInventoryComplete = false,
    IReadOnlyList<SafetyCheck>? SafetyChecks = null, IReadOnlyList<Guid>? ServiceNotRequiredEquipmentIds = null, IReadOnlyList<DeclaredIssue>? Issues = null);
public sealed record AssessmentRecord(Guid PropertyId, HealthAssessment Assessment, Guid UpdatedByUserId, DateTime UpdatedAtUtc);
public sealed record EquipmentEvidence(Guid EquipmentId, string Name, string Category, string Status, DateOnly? LastServicedOn, DateOnly? NextServiceOn);
public sealed record DocumentEvidence(Guid DocumentId, string Type, DateOnly? ExpiresOn, DateTime UploadedAtUtc, DateTime? UpdatedAtUtc);
public sealed record HealthAction(string Code, string Message, Guid? RelatedId = null);
public sealed record HealthComponent(string Code, decimal Weight, decimal? Score, decimal DataCoveragePercent, decimal? WeightedContribution);
public sealed record HealthScoreResult(Guid PropertyId, int? HealthScore, string Status, decimal DataCoveragePercent, DateTime CalculatedAtUtc, string MethodVersion,
    string AssessmentSource, DateTime? AssessmentUpdatedAtUtc, IReadOnlyList<HealthComponent> Components, IReadOnlyList<HealthAction> CriticalAlerts, IReadOnlyList<HealthAction> ImprovementActions);
