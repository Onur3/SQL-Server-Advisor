using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.Application.Contracts;

public interface IIndexAnalysisRule
{
    IReadOnlyCollection<string> RuleIds { get; }

    Task<IReadOnlyCollection<Finding>> EvaluateAsync(
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<IndexSnapshot> indexes,
        IReadOnlyCollection<MissingIndexSnapshot> missingIndexes,
        CancellationToken cancellationToken);
}
