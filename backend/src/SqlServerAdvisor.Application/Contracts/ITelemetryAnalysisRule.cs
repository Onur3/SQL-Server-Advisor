using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Application.Contracts;

public interface ITelemetryAnalysisRule
{
    IReadOnlyCollection<string> RuleIds { get; }

    Task<IReadOnlyCollection<Finding>> EvaluateAsync(
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<WaitSnapshot> waits,
        IReadOnlyCollection<BlockingEvent> blocking,
        CancellationToken cancellationToken);
}
