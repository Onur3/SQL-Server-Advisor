using Microsoft.Extensions.DependencyInjection;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Application.Scoping;
using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.SqlServer.Collectors;

public sealed class TableScopeFilteringCollector(
    IAdvisorCollector inner,
    IServiceScopeFactory scopeFactory) : IAdvisorCollector
{
    public string Name => inner.Name;
    public int DefaultIntervalSeconds => inner.DefaultIntervalSeconds;
    public int DefaultTimeoutSeconds => inner.DefaultTimeoutSeconds;

    public async Task<CollectorBatch> CollectAsync(ServerProfile server, CancellationToken cancellationToken)
    {
        var batch = await inner.CollectAsync(server, cancellationToken);

        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ITableScopeStore>();
        var configured = await store.GetForServerAsync(server.Id, cancellationToken);
        if (configured.Count == 0)
            return batch;

        var selected = configured
            .Select(x => new MonitoredTableSelectionDto(x.DatabaseName, x.SchemaName, x.TableName))
            .ToArray();

        return new CollectorBatch
        {
            ServerSnapshot = batch.ServerSnapshot,
            Databases = batch.Databases,
            DatabaseFiles = batch.DatabaseFiles,
            Configurations = batch.Configurations,
            Waits = batch.Waits,
            FileIo = batch.FileIo,
            TempDb = batch.TempDb,
            Blocking = batch.Blocking,
            Deadlocks = batch.Deadlocks,
            Queries = batch.Queries,
            Indexes = batch.Indexes
                .Where(x => TableScopeMatcher.Allows(selected, x.DatabaseName, x.TableName))
                .ToArray(),
            MissingIndexes = batch.MissingIndexes
                .Where(x => TableScopeMatcher.Allows(selected, x.DatabaseName, x.TableName))
                .ToArray(),
            Statistics = batch.Statistics
                .Where(x => TableScopeMatcher.Allows(selected, x.DatabaseName, x.TableName))
                .ToArray(),
            CodeObjects = batch.CodeObjects,
            Capabilities = batch.Capabilities,
            Warnings = FilterWarnings(batch.Warnings, selected)
        };
    }

    private static IReadOnlyList<string> FilterWarnings(
        IReadOnlyList<string> warnings,
        IReadOnlyCollection<MonitoredTableSelectionDto> selected)
    {
        var selectedDatabases = selected
            .Select(x => x.DatabaseName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return warnings.Where(warning =>
        {
            if (warning.StartsWith("Sunucu:", StringComparison.OrdinalIgnoreCase))
                return true;

            var separator = warning.IndexOf(':');
            if (separator <= 0)
                return true;

            var databaseName = warning[..separator].Trim();
            return selectedDatabases.Contains(databaseName);
        }).ToArray();
    }
}
