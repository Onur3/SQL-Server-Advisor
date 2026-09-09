namespace SqlServerAdvisor.Application.DTOs;

public sealed record DashboardServerDto(
    Guid Id,
    string Name,
    string Host,
    bool Online,
    DateTimeOffset? LastSnapshotAt,
    int? SqlCpuPercent,
    long? AvailableMemoryMb,
    int ActiveSessions,
    int ActiveRequests,
    int BlockedRequests,
    decimal? HealthScore,
    int DataCoveragePercent,
    int OpenFindingCount,
    int HighFindingCount,
    int CriticalFindingCount,
    decimal? TopFindingScore);

public sealed record WorkerStatusDto(
    bool Online,
    string? MachineName,
    string? Version,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastHeartbeatAt);
