using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Application.Contracts;

public interface IMonitoredConnectionStringFactory
{
    string Create(ServerProfile server);
}
