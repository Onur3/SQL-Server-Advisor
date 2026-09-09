using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Application.Contracts;

public interface IAnalysisRule
{
    string RuleId { get; }
    Task<IReadOnlyCollection<Finding>> EvaluateAsync(ServerSnapshot snapshot, CancellationToken cancellationToken);
}
