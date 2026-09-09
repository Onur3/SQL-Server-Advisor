using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Domain.Enums;

namespace SqlServerAdvisor.Analysis.Rules;

public sealed class LowAvailableMemoryRule : IAnalysisRule
{
    public string RuleId => "MEM-001";

    public Task<IReadOnlyCollection<Finding>> EvaluateAsync(ServerSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!snapshot.MemoryMetricsAvailable ||
            snapshot.PhysicalMemoryMb is null || snapshot.PhysicalMemoryMb <= 0 ||
            snapshot.AvailableMemoryMb is null || snapshot.AvailableMemoryMb < 0)
            return Task.FromResult<IReadOnlyCollection<Finding>>([]);

        var availablePercent = snapshot.AvailableMemoryMb.Value * 100m / snapshot.PhysicalMemoryMb.Value;
        if (availablePercent >= 10m)
            return Task.FromResult<IReadOnlyCollection<Finding>>([]);

        var severity = availablePercent <= 3m
            ? FindingSeverity.Critical
            : availablePercent <= 5m ? FindingSeverity.High : FindingSeverity.Medium;

        var impact = Math.Min(100m, 55m + (10m - availablePercent) * 5m);

        IReadOnlyCollection<Finding> result =
        [
            new Finding
            {
                ServerProfileId = snapshot.ServerProfileId,
                RuleId = RuleId,
                Category = "Memory",
                Severity = severity,
                Title = $"Kullanılabilir sistem belleği düşük: %{availablePercent:F1}",
                TechnicalDescription = $"Sunucuda {snapshot.AvailableMemoryMb.Value:N0} MB kullanılabilir bellek kaldı. Toplam fiziksel belleğin yaklaşık %{availablePercent:F1} kadarı kullanılabilir durumda. SQL Server max server memory ayarı, işletim sistemi rezervi ve diğer süreçlerin bellek tüketimi birlikte incelenmelidir.",
                ConfidenceScore = 95m,
                ImpactScore = impact,
                FindingScore = impact,
                Fingerprint = $"MEM-001:{snapshot.ServerProfileId}",
                FirstDetectedAt = snapshot.CapturedAt,
                LastDetectedAt = snapshot.CapturedAt
            }
        ];

        return Task.FromResult(result);
    }
}
