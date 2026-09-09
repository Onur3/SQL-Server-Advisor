using Microsoft.Extensions.DependencyInjection;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Analysis.Rules;

namespace SqlServerAdvisor.Analysis;

public static class DependencyInjection
{
    public static IServiceCollection AddAdvisorAnalysis(this IServiceCollection services)
    {
        services.AddSingleton<IAnalysisRule, BlockingPressureRule>();
        return services;
    }
}
