namespace SqlServerAdvisor.Application.DTOs;

public sealed record WaitTelemetryDto(
    Guid ServerProfileId,
    string ServerName,
    DateTimeOffset CapturedAt,
    string WaitType,
    long WaitingTasks,
    long WaitTimeMs,
    long DeltaWaitTimeMs,
    long DeltaSignalWaitTimeMs);

public sealed record BlockingTelemetryDto(
    Guid ServerProfileId,
    string ServerName,
    DateTimeOffset CapturedAt,
    int SessionId,
    int BlockingSessionId,
    string? DatabaseName,
    string? WaitType,
    long WaitTimeMs,
    string? WaitResource,
    string? SqlText,
    string? HostName,
    string? ProgramName,
    string? LoginName);
