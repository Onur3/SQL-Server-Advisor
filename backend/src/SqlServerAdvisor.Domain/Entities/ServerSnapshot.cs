namespace SqlServerAdvisor.Domain.Entities;

public sealed class ServerSnapshot
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public string? ServerName { get; set; }
    public string? ProductVersion { get; set; }
    public string? Edition { get; set; }
    public DateTimeOffset? SqlServerStartTime { get; set; }
    public int? SqlCpuPercent { get; set; }
    public int? SystemIdlePercent { get; set; }
    public long? PhysicalMemoryMb { get; set; }
    public long? AvailableMemoryMb { get; set; }
    public long? SqlMemoryMb { get; set; }
    public long? PageLifeExpectancy { get; set; }
    public int ActiveSessions { get; set; }
    public int ActiveRequests { get; set; }
    public int BlockedRequests { get; set; }
    public int UserConnections { get; set; }
    public bool SessionMetricsAvailable { get; set; }
    public bool MemoryMetricsAvailable { get; set; }
    public bool CpuMetricsAvailable { get; set; }
}
