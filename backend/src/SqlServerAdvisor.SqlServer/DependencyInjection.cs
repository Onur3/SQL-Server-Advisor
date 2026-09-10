using Microsoft.Extensions.DependencyInjection;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.SqlServer.Capability;
using SqlServerAdvisor.SqlServer.Collectors;
using SqlServerAdvisor.SqlServer.Connection;

namespace SqlServerAdvisor.SqlServer;

public static class DependencyInjection
{
    public static IServiceCollection AddMonitoredSqlServer(this IServiceCollection services)
    {
        services.AddSingleton<IMonitoredConnectionStringFactory, MonitoredConnectionStringFactory>();
        services.AddSingleton<IServerCapabilityScanner, ServerCapabilityScanner>();
        services.AddSingleton<IServerHealthCollector, ServerHealthCollector>();
        services.AddSingleton<IAdvisorCollector, WaitBlockingCollector>();
        services.AddSingleton<IAdvisorCollector, QueryPerformanceCollector>();

        services.AddSingleton<IndexAdvisorCollector>();
        services.AddSingleton<StatisticsAdvisorCollector>();
        services.AddSingleton<IAdvisorCollector>(sp => new TableScopeFilteringCollector(
            sp.GetRequiredService<IndexAdvisorCollector>(),
            sp.GetRequiredService<IServiceScopeFactory>()));
        services.AddSingleton<IAdvisorCollector>(sp => new TableScopeFilteringCollector(
            sp.GetRequiredService<StatisticsAdvisorCollector>(),
            sp.GetRequiredService<IServiceScopeFactory>()));

        services.AddSingleton<DeadlockCollector>();
        return services;
    }
}
