using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Application.Contracts;

public interface IDeadlockAnalysisRule
{
    IReadOnlyCollection<string> RuleIds { get; }

    Task<IReadOnlyCollection<Finding>> EvaluateAsync(
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<DeadlockEvent> deadlocks,
        CancellationToken cancellationToken);
}
