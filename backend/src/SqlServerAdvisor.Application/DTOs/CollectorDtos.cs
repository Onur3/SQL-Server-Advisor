using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Application.DTOs;

public sealed record WaitObservation(string WaitType, long WaitingTasks, long WaitTimeMs, long SignalWaitTimeMs);

public sealed record FileIoObservation(
    int DatabaseId,
    string DatabaseName,
    int FileId,
    string LogicalName,
    string TypeDesc,
    long NumReads,
    long NumWrites,
    long IoStallReadMs,
    long IoStallWriteMs,
    long BytesRead,
    long BytesWritten);

public sealed record QueryObservation(
    string DatabaseName,
    string QueryHash,
    string NormalizedHash,
    int? ObjectId,
    string? ObjectName,
    string StatementText,
    string Source,
    string? PlanHash,
    string? PlanXml,
    bool HasActualRuntimeCounters,
    long ExecutionCount,
    decimal TotalCpuMs,
    decimal AverageCpuMs,
    decimal TotalDurationMs,
    decimal AverageDurationMs,
    long TotalLogicalReads,
    decimal AverageLogicalReads,
    long TotalLogicalWrites,
    DateTimeOffset? LastExecutionTime);

public sealed record CodeObjectObservation(
    string DatabaseName,
    int ObjectId,
    string SchemaName,
    string ObjectName,
    string ObjectType,
    DateTime ModifyDate,
    string DefinitionHash,
    string DefinitionText);

public sealed class CollectorBatch
{
    public ServerSnapshot? ServerSnapshot { get; init; }
    public IReadOnlyList<DatabaseSnapshot> Databases { get; init; } = [];
    public IReadOnlyList<DatabaseFileSnapshot> DatabaseFiles { get; init; } = [];
    public IReadOnlyList<ConfigurationSnapshot> Configurations { get; init; } = [];
    public IReadOnlyList<WaitObservation> Waits { get; init; } = [];
    public IReadOnlyList<FileIoObservation> FileIo { get; init; } = [];
    public IReadOnlyList<TempDbSnapshot> TempDb { get; init; } = [];
    public IReadOnlyList<BlockingEvent> Blocking { get; init; } = [];
    public IReadOnlyList<DeadlockEvent> Deadlocks { get; init; } = [];
    public IReadOnlyList<QueryObservation> Queries { get; init; } = [];
    public IReadOnlyList<IndexSnapshot> Indexes { get; init; } = [];
    public IReadOnlyList<MissingIndexSnapshot> MissingIndexes { get; init; } = [];
    public IReadOnlyList<StatisticsSnapshot> Statistics { get; init; } = [];
    public IReadOnlyList<CodeObjectObservation> CodeObjects { get; init; } = [];
    public IReadOnlyList<ServerCapability> Capabilities { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public int RowCount =>
        (ServerSnapshot is null ? 0 : 1) + Databases.Count + DatabaseFiles.Count + Configurations.Count +
        Waits.Count + FileIo.Count + TempDb.Count + Blocking.Count + Deadlocks.Count + Queries.Count +
        Indexes.Count + MissingIndexes.Count + Statistics.Count + CodeObjects.Count + Capabilities.Count;
}

public sealed record RecommendationDraft(
    string Title,
    string Explanation,
    string ExpectedBenefit,
    string RiskLevel,
    decimal ConfidenceScore,
    string RecommendedAction,
    string? ScriptText = null);

public sealed record EvidenceDraft(
    string Metric,
    string ObservedValue,
    string? ExpectedValue,
    string? Unit,
    string Source,
    string Description);
