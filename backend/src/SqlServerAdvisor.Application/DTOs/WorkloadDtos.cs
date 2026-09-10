namespace SqlServerAdvisor.Application.DTOs;

public sealed record WorkloadSettingsDto(
    bool Enabled,
    string FolderPath,
    bool Recursive,
    Guid? DefaultServerProfileId,
    string? DefaultDatabaseName,
    int ScanIntervalSeconds,
    int MaxFileSizeKb,
    string LastStatus,
    string? LastMessage,
    DateTimeOffset? LastScanAt,
    int ActiveFiles);

public sealed record UpdateWorkloadSettingsRequest(
    bool Enabled,
    string FolderPath,
    bool Recursive,
    Guid? DefaultServerProfileId,
    string? DefaultDatabaseName,
    int ScanIntervalSeconds,
    int MaxFileSizeKb);

public sealed record WorkloadFileDto(
    long Id,
    Guid? ServerProfileId,
    string FilePath,
    string FileName,
    string? DatabaseName,
    string ReferencedObjects,
    string ReferencedColumns,
    DateTimeOffset LastWriteTimeUtc,
    DateTimeOffset LastScannedAt,
    bool IsActive,
    string? ParseMessage);

public sealed record AiPromptDto(
    string Prompt,
    DateTimeOffset GeneratedAt);
