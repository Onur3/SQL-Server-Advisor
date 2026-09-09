namespace SqlServerAdvisor.Domain.Entities;

public sealed class CollectorRun
{
    public long Id { get; set; }
    public Guid ServerProfileId { get; set; }
    public string CollectorType { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Status { get; set; } = "Running";
    public int RowsCollected { get; set; }
    public long? DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}
