using SqlServerAdvisor.Application.DTOs;

namespace SqlServerAdvisor.Application.Contracts;

public interface ITableScopeStore
{
    Task<IReadOnlyList<MonitoredTableScopeItemDto>> GetAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<MonitoredTableScopeItemDto>> GetForServerAsync(Guid serverProfileId, CancellationToken cancellationToken);
    Task ReplaceForServerAsync(Guid serverProfileId, IReadOnlyCollection<MonitoredTableSelectionDto> tables, CancellationToken cancellationToken);
}
