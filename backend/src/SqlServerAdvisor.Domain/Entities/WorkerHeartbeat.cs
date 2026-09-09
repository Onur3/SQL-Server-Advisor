namespace SqlServerAdvisor.Domain.Entities;

public sealed class WorkerHeartbeat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MachineName { get; set; } = Environment.MachineName;
    public int ProcessId { get; set; }
    public string Version { get; set; } = "1.0.0";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastHeartbeatAt { get; set; }
    public string Status { get; set; } = "Online";
}
