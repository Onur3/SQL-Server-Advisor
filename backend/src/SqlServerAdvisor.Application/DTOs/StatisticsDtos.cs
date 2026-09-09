namespace SqlServerAdvisor.Application.DTOs;

public sealed record StatisticsStatusDto(
    long Id,
    Guid ServerProfileId,
    string ServerName,
    string DatabaseName,
    string TableName,
    string StatisticsName,
    long Rows,
    long RowsSampled,
    long ModificationCounter,
    decimal ModificationPercent,
    DateTime? LastUpdated,
    decimal? SamplePercent,
    DateTimeOffset CapturedAt);
