using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Domain.Enums;

namespace SqlServerAdvisor.Analysis.Rules;

public sealed class BlockingPressureRule : IAnalysisRule
{
    public string RuleId => "BLK-001";

    public Task<IReadOnlyCollection<Finding>> EvaluateAsync(ServerSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (snapshot.BlockedRequests <= 0)
            return Task.FromResult<IReadOnlyCollection<Finding>>([]);

        var severity = snapshot.BlockedRequests >= 10
            ? FindingSeverity.Critical
            : snapshot.BlockedRequests >= 3 ? FindingSeverity.High : FindingSeverity.Medium;

        var impact = Math.Min(100m, 35m + snapshot.BlockedRequests * 6m);
        var confidence = 100m;

        IReadOnlyCollection<Finding> result =
        [
            new Finding
            {
                ServerProfileId = snapshot.ServerProfileId,
                RuleId = RuleId,
                Category = "Blocking",
                Severity = severity,
                Title = $"{snapshot.BlockedRequests} adet bloklanan aktif istek tespit edildi",
                TechnicalDescription = "sys.dm_exec_requests içinde blocking_session_id > 0 olan aktif istekler tespit edildi. Head blocker ve blocking chain ayrıntısı gelişmiş collector aşamasında ilişkilendirilecektir.",
                ConfidenceScore = confidence,
                ImpactScore = impact,
                FindingScore = impact,
                Fingerprint = $"BLK-001:{snapshot.ServerProfileId}",
                FirstDetectedAt = snapshot.CapturedAt,
                LastDetectedAt = snapshot.CapturedAt
            }
        ];

        return Task.FromResult(result);
    }
}
