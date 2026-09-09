namespace SqlServerAdvisor.Domain.Entities;

public sealed class QueryDefinition
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string QueryHash { get; set; } = string.Empty;
    public string NormalizedHash { get; set; } = string.Empty;
    public int? ObjectId { get; set; }
    public string? ObjectName { get; set; }
    public string StatementText { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class QueryPlan
{
    public long Id { get; set; }
    public long QueryId { get; set; }
    public string PlanHash { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public bool HasActualRuntimeCounters { get; set; }
    public string PlanXml { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class QueryRuntimeSnapshot
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public long QueryId { get; set; }
    public long? PlanId { get; set; }
    public string Source { get; set; } = string.Empty;
    public long ExecutionCount { get; set; }
    public decimal TotalCpuMs { get; set; }
    public decimal AverageCpuMs { get; set; }
    public decimal TotalDurationMs { get; set; }
    public decimal AverageDurationMs { get; set; }
    public long TotalLogicalReads { get; set; }
    public decimal AverageLogicalReads { get; set; }
    public long TotalLogicalWrites { get; set; }
    public decimal ImpactScore { get; set; }
    public DateTimeOffset? LastExecutionTime { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}

public sealed class IndexSnapshot
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public int ObjectId { get; set; }
    public int IndexId { get; set; }
    public string TableName { get; set; } = string.Empty;
    public string IndexName { get; set; } = string.Empty;
    public string TypeDesc { get; set; } = string.Empty;
    public string KeyColumns { get; set; } = string.Empty;
    public string IncludeColumns { get; set; } = string.Empty;
    public decimal SizeMb { get; set; }
    public long UserSeeks { get; set; }
    public long UserScans { get; set; }
    public long UserLookups { get; set; }
    public long UserUpdates { get; set; }
    public bool IsUnique { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsDisabled { get; set; }
    public decimal? AvgFragmentationPercent { get; set; }
    public long? PageCount { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}

public sealed class MissingIndexSnapshot
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string EqualityColumns { get; set; } = string.Empty;
    public string InequalityColumns { get; set; } = string.Empty;
    public string IncludedColumns { get; set; } = string.Empty;
    public long UserSeeks { get; set; }
    public long UserScans { get; set; }
    public decimal AvgTotalUserCost { get; set; }
    public decimal AvgUserImpact { get; set; }
    public decimal ImprovementMeasure { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}

public sealed class StatisticsSnapshot
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public int ObjectId { get; set; }
    public int StatisticsId { get; set; }
    public string TableName { get; set; } = string.Empty;
    public string StatisticsName { get; set; } = string.Empty;
    public long Rows { get; set; }
    public long RowsSampled { get; set; }
    public long ModificationCounter { get; set; }
    public DateTime? LastUpdated { get; set; }
    public decimal? SamplePercent { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}

public sealed class CodeObjectSnapshot
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public int ObjectId { get; set; }
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public DateTime ModifyDate { get; set; }
    public string DefinitionHash { get; set; } = string.Empty;
    public string DefinitionText { get; set; } = string.Empty;
    public bool ParseSucceeded { get; set; }
    public string? ParseMessage { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}
