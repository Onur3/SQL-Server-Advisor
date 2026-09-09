using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Domain.Enums;

namespace SqlServerAdvisor.Analysis.Rules;

public sealed class HighSqlCpuRule : IAnalysisRule
{
    public string RuleId => "CPU-001";

    public Task<IReadOnlyCollection<Finding>> EvaluateAsync(ServerSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!snapshot.CpuMetricsAvailable || snapshot.SqlCpuPercent is null || snapshot.SqlCpuPercent < 80)
            return Task.FromResult<IReadOnlyCollection<Finding>>([]);

        var cpu = snapshot.SqlCpuPercent.Value;
        var severity = cpu >= 95
            ? FindingSeverity.Critical
            : cpu >= 90 ? FindingSeverity.High : FindingSeverity.Medium;

        var impact = Math.Min(100m, 50m + (cpu - 80m) * 2.5m);

        IReadOnlyCollection<Finding> result =
        [
            new Finding
            {
                ServerProfileId = snapshot.ServerProfileId,
                RuleId = RuleId,
                Category = "CPU",
                Severity = severity,
                Title = $"SQL Server CPU kullanımı yüksek: %{cpu}",
                TechnicalDescription = "SQL Server işlemci kullanımı son snapshot sırasında yüksek ölçüldü. Sürekli tekrar eden durumlarda en yüksek CPU tüketen sorgular, execution planları, paralellik ayarları ve sunucu üzerindeki SQL dışı CPU yükleri birlikte incelenmelidir.",
                ConfidenceScore = 95m,
                ImpactScore = impact,
                FindingScore = impact,
                Fingerprint = $"CPU-001:{snapshot.ServerProfileId}",
                FirstDetectedAt = snapshot.CapturedAt,
                LastDetectedAt = snapshot.CapturedAt
            }
        ];

        return Task.FromResult(result);
    }
}
