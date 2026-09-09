namespace SqlServerAdvisor.Domain.Entities;

public sealed class FindingEvidence
{
    public long Id { get; set; }
    public long FindingId { get; set; }
    public string Metric { get; set; } = string.Empty;
    public string ObservedValue { get; set; } = string.Empty;
    public string? ExpectedValue { get; set; }
    public string? Unit { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class FindingRelation
{
    public long Id { get; set; }
    public long ParentFindingId { get; set; }
    public long ChildFindingId { get; set; }
    public string RelationType { get; set; } = string.Empty;
    public decimal ConfidenceScore { get; set; }
}

public sealed class RecommendationValidation
{
    public long Id { get; set; }
    public long RecommendationId { get; set; }
    public DateTimeOffset ValidationStartedAt { get; set; }
    public DateTimeOffset? ValidationCompletedAt { get; set; }
    public decimal? BeforeDurationMs { get; set; }
    public decimal? AfterDurationMs { get; set; }
    public decimal? BeforeCpuMs { get; set; }
    public decimal? AfterCpuMs { get; set; }
    public decimal? BeforeLogicalReads { get; set; }
    public decimal? AfterLogicalReads { get; set; }
    public int BeforeSampleCount { get; set; }
    public int AfterSampleCount { get; set; }
    public string Status { get; set; } = "Collecting";
    public string? Result { get; set; }
    public string? Detail { get; set; }
}

public sealed class AlertEvent
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public long? FindingId { get; set; }
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
}

public sealed class UserAccess
{
    public long Id { get; set; }
    public string WindowsIdentity { get; set; } = string.Empty;
    public string Role { get; set; } = "Viewer";
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AuditEvent
{
    public long Id { get; set; }
    public string UserIdentity { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ServerHourlyAggregate
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public DateTimeOffset HourStart { get; set; }
    public decimal? AvgSqlCpuPercent { get; set; }
    public decimal? AvgAvailableMemoryMb { get; set; }
    public decimal AvgActiveSessions { get; set; }
    public decimal AvgBlockedRequests { get; set; }
    public int SampleCount { get; set; }
}
