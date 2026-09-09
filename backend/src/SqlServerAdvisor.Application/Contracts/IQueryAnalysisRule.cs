using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Application.Contracts;

public interface IQueryAnalysisRule
{
    IReadOnlyCollection<string> RuleIds { get; }

    Task<IReadOnlyCollection<Finding>> EvaluateAsync(
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        QueryDefinition query,
        QueryRuntimeSnapshot runtime,
        CancellationToken cancellationToken);
}
