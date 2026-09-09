using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Application.Contracts;

public interface IAdvisorCollector
{
    string Name { get; }
    int DefaultIntervalSeconds { get; }
    int DefaultTimeoutSeconds { get; }
    Task<CollectorBatch> CollectAsync(ServerProfile server, CancellationToken cancellationToken);
}
