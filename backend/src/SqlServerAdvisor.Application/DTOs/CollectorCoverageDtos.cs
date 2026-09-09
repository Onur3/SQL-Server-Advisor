namespace SqlServerAdvisor.Application.DTOs;

public sealed record CollectorCoverageDto(
    Guid ServerProfileId,
    string ServerName,
    string CollectorType,
    string Status,
    int RowsCollected,
    string? WarningMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);
