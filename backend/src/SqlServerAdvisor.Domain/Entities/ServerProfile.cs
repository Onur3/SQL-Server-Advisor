using SqlServerAdvisor.Domain.Enums;

namespace SqlServerAdvisor.Domain.Entities;

public sealed class ServerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1433;
    public string DefaultDatabase { get; set; } = "master";
    public ServerAuthenticationType AuthenticationType { get; set; } = ServerAuthenticationType.Windows;
    public string? Username { get; set; }
    public string? ProtectedPassword { get; set; }
    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastConnectedAt { get; set; }
    public string? LastError { get; set; }
}
