namespace SqlServerAdvisor.Domain.Entities;

public sealed class ApplicationSetting
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WorkloadQueryFile
{
    public long Id { get; set; }
    public Guid? ServerProfileId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? DatabaseName { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string NormalizedHash { get; set; } = string.Empty;
    public string QueryText { get; set; } = string.Empty;
    public string ReferencedObjects { get; set; } = string.Empty;
    public string PredicateColumns { get; set; } = string.Empty;
    public DateTimeOffset LastWriteTimeUtc { get; set; }
    public DateTimeOffset LastScannedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ParseMessage { get; set; }
}
