using SqlServerAdvisor.Domain.Enums;

namespace SqlServerAdvisor.Application.DTOs;

public sealed record ServerListItemDto(
    Guid Id,
    string Name,
    string Host,
    int Port,
    string DefaultDatabase,
    ServerAuthenticationType AuthenticationType,
    string? Username,
    bool Encrypt,
    bool TrustServerCertificate,
    bool IsEnabled,
    DateTimeOffset? LastConnectedAt,
    string? LastError);

public sealed record CreateServerRequest(
    string Name,
    string Host,
    int Port,
    string DefaultDatabase,
    ServerAuthenticationType AuthenticationType,
    string? Username,
    string? Password,
    bool Encrypt,
    bool TrustServerCertificate);

public sealed record ConnectionTestResult(
    bool Success,
    string? ServerName,
    string? ProductVersion,
    string? Edition,
    string? Error);
