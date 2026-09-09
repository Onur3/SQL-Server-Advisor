namespace SqlServerAdvisor.Application.Contracts;

public interface IAuditWriter
{
    Task WriteAsync(string userIdentity, string action, string? targetType, string? targetId, string? detail, CancellationToken cancellationToken = default);
}
