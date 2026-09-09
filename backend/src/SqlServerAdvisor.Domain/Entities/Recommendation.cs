namespace SqlServerAdvisor.Domain.Entities;

public sealed class Recommendation
{
    public long Id { get; set; }
    public long FindingId { get; set; }
    public decimal PriorityScore { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string ExpectedBenefit { get; set; } = "Unknown";
    public string RiskLevel { get; set; } = "Low";
    public decimal ConfidenceScore { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
    public string? ScriptText { get; set; }
    public bool CanExecute { get; set; } = false;
    public string Status { get; set; } = "New";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset? ImplementedAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? WorkflowNote { get; set; }
}
