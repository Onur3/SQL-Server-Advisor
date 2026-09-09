using SqlServerAdvisor.Domain.Enums;

namespace SqlServerAdvisor.Domain.Entities;

public sealed class Finding
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public long? QueryId { get; set; }
    public string? DatabaseName { get; set; }
    public string? ObjectName { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public FindingSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string TechnicalDescription { get; set; } = string.Empty;
    public decimal ConfidenceScore { get; set; }
    public decimal ImpactScore { get; set; }
    public decimal FindingScore { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public DateTimeOffset FirstDetectedAt { get; set; }
    public DateTimeOffset LastDetectedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public int OccurrenceCount { get; set; } = 1;
    public string Status { get; set; } = "Open";
}
