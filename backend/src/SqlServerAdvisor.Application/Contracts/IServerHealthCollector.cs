using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Application.Contracts;

public interface IServerHealthCollector
{
    Task<ServerSnapshot> CollectAsync(ServerProfile server, CancellationToken cancellationToken);
}
