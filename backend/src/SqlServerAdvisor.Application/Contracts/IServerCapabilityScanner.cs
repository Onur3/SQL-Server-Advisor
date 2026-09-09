using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Application.Contracts;

public interface IServerCapabilityScanner
{
    Task<ConnectionTestResult> TestAsync(ServerProfile server, CancellationToken cancellationToken);
}
