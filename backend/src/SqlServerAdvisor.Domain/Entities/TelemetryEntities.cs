namespace SqlServerAdvisor.Domain.Entities;

public sealed class CollectorSetting
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public string CollectorType { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int IntervalSeconds { get; set; }
    public int TimeoutSeconds { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ServerCapability
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public string CapabilityKey { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
}

public sealed class DatabaseSnapshot
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public int DatabaseId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string StateDesc { get; set; } = string.Empty;
    public string RecoveryModelDesc { get; set; } = string.Empty;
    public short CompatibilityLevel { get; set; }
    public bool IsAutoCloseOn { get; set; }
    public bool IsAutoShrinkOn { get; set; }
    public bool IsAutoCreateStatsOn { get; set; }
    public bool IsAutoUpdateStatsOn { get; set; }
    public string PageVerifyOptionDesc { get; set; } = string.Empty;
    public string? QueryStoreStateDesc { get; set; }
    public decimal SizeMb { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}

public sealed class DatabaseFileSnapshot
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public int FileId { get; set; }
    public string LogicalName { get; set; } = string.Empty;
    public string TypeDesc { get; set; } = string.Empty;
    public string PhysicalName { get; set; } = string.Empty;
    public decimal SizeMb { get; set; }
    public decimal? MaxSizeMb { get; set; }
    public bool IsPercentGrowth { get; set; }
    public decimal GrowthValue { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}

public sealed class ConfigurationSnapshot
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long ConfiguredValue { get; set; }
    public long RunningValue { get; set; }
    public bool IsDynamic { get; set; }
    public bool IsAdvanced { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}

public sealed class WaitSnapshot
{
    public DateTime? SqlServerStartTime { get; set; }
    public bool IsBaseline { get; set; }
    public long IntervalMs { get; set; }
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public string WaitType { get; set; } = string.Empty;
    public long WaitingTasks { get; set; }
    public long WaitTimeMs { get; set; }
    public long SignalWaitTimeMs { get; set; }
    public long DeltaWaitTimeMs { get; set; }
    public long DeltaSignalWaitTimeMs { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}

public sealed class FileIoSnapshot
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public int DatabaseId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public int FileId { get; set; }
    public string LogicalName { get; set; } = string.Empty;
    public string TypeDesc { get; set; } = string.Empty;
    public long NumReads { get; set; }
    public long NumWrites { get; set; }
    public long IoStallReadMs { get; set; }
    public long IoStallWriteMs { get; set; }
    public long BytesRead { get; set; }
    public long BytesWritten { get; set; }
    public long DeltaReads { get; set; }
    public long DeltaWrites { get; set; }
    public long DeltaReadStallMs { get; set; }
    public long DeltaWriteStallMs { get; set; }
    public decimal? ReadLatencyMs { get; set; }
    public decimal? WriteLatencyMs { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}

public sealed class TempDbSnapshot
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public decimal UserObjectMb { get; set; }
    public decimal InternalObjectMb { get; set; }
    public decimal VersionStoreMb { get; set; }
    public decimal FreeSpaceMb { get; set; }
    public decimal TotalMb { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}

public sealed class BlockingEvent
{
    public int RequestId { get; set; }
    public string? BlockerStatus { get; set; }
    public string? BlockerSqlText { get; set; }
    public int? BlockerOpenTransactions { get; set; }
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public int SessionId { get; set; }
    public int BlockingSessionId { get; set; }
    public string? DatabaseName { get; set; }
    public string? WaitType { get; set; }
    public long WaitTimeMs { get; set; }
    public string? WaitResource { get; set; }
    public string? SqlText { get; set; }
    public string? HostName { get; set; }
    public string? ProgramName { get; set; }
    public string? LoginName { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}

public sealed class DeadlockEvent
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public DateTimeOffset EventTime { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public string? VictimProcessId { get; set; }
    public string? DatabaseNames { get; set; }
    public string? ObjectNames { get; set; }
    public string DeadlockXml { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; }
}
