namespace SqlServerAdvisor.Application.DTOs;

public sealed record FindingListItemDto(
    long Id,
    Guid ServerProfileId,
    string ServerName,
    string RuleId,
    string Category,
    int Severity,
    string Title,
    string TechnicalDescription,
    decimal ConfidenceScore,
    decimal ImpactScore,
    decimal FindingScore,
    DateTimeOffset FirstDetectedAt,
    DateTimeOffset LastDetectedAt,
    DateTimeOffset? ResolvedAt,
    int OccurrenceCount,
    string Status);

public sealed record RecommendationListItemDto(
    long Id,
    long FindingId,
    Guid ServerProfileId,
    string ServerName,
    string RuleId,
    int Severity,
    string FindingTitle,
    decimal PriorityScore,
    string Title,
    string Explanation,
    string ExpectedBenefit,
    string RiskLevel,
    decimal ConfidenceScore,
    string RecommendedAction,
    string? ScriptText,
    bool CanExecute,
    string Status,
    DateTimeOffset CreatedAt);
