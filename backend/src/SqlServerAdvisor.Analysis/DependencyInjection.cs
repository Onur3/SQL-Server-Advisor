using Microsoft.Extensions.DependencyInjection;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Analysis.Recommendations;
using SqlServerAdvisor.Analysis.Rules;

namespace SqlServerAdvisor.Analysis;

public static class DependencyInjection
{
    public static IServiceCollection AddAdvisorAnalysis(this IServiceCollection services)
    {
        services.AddSingleton<IAnalysisRule, BlockingPressureRule>();
        services.AddSingleton<IAnalysisRule, HighSqlCpuRule>();
        services.AddSingleton<IAnalysisRule, LowAvailableMemoryRule>();
        services.AddSingleton<ITelemetryAnalysisRule, WaitBlockingPressureRule>();
        services.AddSingleton<IQueryAnalysisRule, ExpensiveQueryRule>();
        services.AddSingleton<IIndexAnalysisRule, IndexAdvisorRule>();
        services.AddSingleton<IRecommendationFactory, RecommendationFactory>();
        return services;
    }
}
