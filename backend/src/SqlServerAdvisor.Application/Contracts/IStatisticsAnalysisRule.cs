using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Application.Contracts;

public interface IStatisticsAnalysisRule
{
    IReadOnlyCollection<string> RuleIds { get; }

    Task<IReadOnlyCollection<Finding>> EvaluateAsync(
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<StatisticsSnapshot> statistics,
        CancellationToken cancellationToken);
}
