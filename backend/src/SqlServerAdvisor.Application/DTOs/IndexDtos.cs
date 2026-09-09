namespace SqlServerAdvisor.Application.DTOs;

public sealed record FragmentedIndexDto(
    long Id,
    Guid ServerProfileId,
    string ServerName,
    string DatabaseName,
    string TableName,
    string IndexName,
    string TypeDesc,
    string KeyColumns,
    string IncludeColumns,
    decimal SizeMb,
    long UserSeeks,
    long UserScans,
    long UserLookups,
    long UserUpdates,
    decimal? AvgFragmentationPercent,
    long? PageCount,
    DateTimeOffset CapturedAt);

public sealed record MissingIndexDto(
    long Id,
    Guid ServerProfileId,
    string ServerName,
    string DatabaseName,
    string TableName,
    string EqualityColumns,
    string InequalityColumns,
    string IncludedColumns,
    long UserSeeks,
    long UserScans,
    decimal AvgTotalUserCost,
    decimal AvgUserImpact,
    decimal ImprovementMeasure,
    DateTimeOffset CapturedAt);
