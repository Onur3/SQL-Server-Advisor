using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Domain.Enums;

namespace SqlServerAdvisor.Analysis.Rules;

public sealed class DeadlockAdvisorRule : IDeadlockAnalysisRule
{
    public IReadOnlyCollection<string> RuleIds => ["DLK-001"];

    public Task<IReadOnlyCollection<Finding>> EvaluateAsync(
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<DeadlockEvent> deadlocks,
        CancellationToken cancellationToken)
    {
        var findings = deadlocks
            .GroupBy(x => x.Fingerprint, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var latest = group.OrderByDescending(x => x.EventTime).First();
                var count = group.Count();
                var impact = Math.Min(100m, 75m + Math.Min(25m, (count - 1) * 8m));

                return new Finding
                {
                    ServerProfileId = serverProfileId,
                    DatabaseName = latest.DatabaseNames,
                    ObjectName = latest.ObjectNames,
                    RuleId = "DLK-001",
                    Category = "Deadlock",
                    Severity = count >= 3 ? FindingSeverity.Critical : FindingSeverity.High,
                    Title = count > 1
                        ? $"Tekrarlayan deadlock pattern'i ({count} olay)"
                        : "Deadlock tespit edildi",
                    TechnicalDescription = $"system_health Extended Events içinde deadlock bulundu. EventTime={latest.EventTime:O}, Victim={latest.VictimProcessId ?? "bilinmiyor"}, Databases={latest.DatabaseNames ?? "bilinmiyor"}, Objects={latest.ObjectNames ?? "bilinmiyor"}. Aynı pattern için XML/SQL kaynakları incelenmelidir; Advisor session kill veya üretim değişikliği uygulamaz.",
                    ConfidenceScore = 100m,
                    ImpactScore = impact,
                    FindingScore = impact,
                    Fingerprint = $"DLK-001:{serverProfileId:N}:{latest.Fingerprint}",
                    FirstDetectedAt = group.Min(x => x.EventTime),
                    LastDetectedAt = group.Max(x => x.EventTime)
                };
            })
            .Cast<Finding>()
            .ToList();

        return Task.FromResult<IReadOnlyCollection<Finding>>(findings);
    }
}
