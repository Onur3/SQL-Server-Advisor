namespace SqlServerAdvisor.Application.DTOs;

public sealed record QueryPerformanceDto(
    long QueryId,
    Guid ServerProfileId,
    string ServerName,
    string DatabaseName,
    string QueryHash,
    string? ObjectName,
    string StatementText,
    string Source,
    long ExecutionCount,
    decimal TotalCpuMs,
    decimal AverageCpuMs,
    decimal TotalDurationMs,
    decimal AverageDurationMs,
    long TotalLogicalReads,
    decimal AverageLogicalReads,
    long TotalLogicalWrites,
    decimal ImpactScore,
    string DiagnosticLevel,
    string DiagnosticHeadline,
    string DiagnosticSummary,
    string SuggestedInspection,
    DateTimeOffset? LastExecutionTime,
    DateTimeOffset CapturedAt,
    long? PlanId,
    string? PlanHash);

public sealed record QueryPlanDto(
    long QueryId,
    long PlanId,
    string PlanHash,
    string Source,
    bool HasActualRuntimeCounters,
    string PlanXml,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);
