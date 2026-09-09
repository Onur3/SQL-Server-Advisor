using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Domain.Enums;

namespace SqlServerAdvisor.Analysis.Rules;

public sealed class WaitBlockingPressureRule : ITelemetryAnalysisRule
{
    public IReadOnlyCollection<string> RuleIds => ["WAIT-001", "BLK-002"];

    public Task<IReadOnlyCollection<Finding>> EvaluateAsync(
        Guid serverProfileId,
        DateTimeOffset capturedAt,
        IReadOnlyCollection<WaitSnapshot> waits,
        IReadOnlyCollection<BlockingEvent> blocking,
        CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();

        foreach (var wait in waits.Where(x => x.DeltaWaitTimeMs > 0))
        {
            if (!TryClassifyWait(wait.WaitType, wait.DeltaWaitTimeMs, out var severity, out var impact, out var label))
                continue;

            findings.Add(new Finding
            {
                ServerProfileId = serverProfileId,
                RuleId = "WAIT-001",
                Category = "WaitStats",
                Severity = severity,
                Title = $"{label}: {wait.WaitType}",
                TechnicalDescription = $"Son örnekleme aralığında {wait.WaitType} için {wait.DeltaWaitTimeMs:N0} ms yeni bekleme birikti. Kümülatif wait süresi {wait.WaitTimeMs:N0} ms, signal wait {wait.DeltaSignalWaitTimeMs:N0} ms arttı. Bu bulgu tek başına değişiklik uygulamaz; sorgu, I/O, transaction ve kapasite verileriyle birlikte değerlendirilmelidir.",
                ConfidenceScore = 90m,
                ImpactScore = impact,
                FindingScore = impact,
                Fingerprint = $"WAIT-001:{serverProfileId}:{wait.WaitType}",
                FirstDetectedAt = capturedAt,
                LastDetectedAt = capturedAt
            });
        }

        foreach (var group in blocking.GroupBy(x => x.BlockingSessionId))
        {
            var maxWaitMs = group.Max(x => x.WaitTimeMs);
            var blockedCount = group.Count();
            var severity = maxWaitMs >= 30_000 ? FindingSeverity.Critical
                : maxWaitMs >= 10_000 ? FindingSeverity.High
                : maxWaitMs >= 3_000 ? FindingSeverity.Medium
                : FindingSeverity.Low;
            var impact = Math.Min(100m, 30m + blockedCount * 8m + Math.Min(50m, maxWaitMs / 1000m));
            var databases = group.Select(x => x.DatabaseName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();

            findings.Add(new Finding
            {
                ServerProfileId = serverProfileId,
                RuleId = "BLK-002",
                Category = "Blocking",
                Severity = severity,
                Title = $"Session {group.Key}, {blockedCount} isteği blokluyor",
                TechnicalDescription = $"Head blocker session {group.Key} için {blockedCount} aktif blocked request görüldü. En uzun mevcut bekleme {maxWaitMs:N0} ms. Bekleme türleri: {string.Join(", ", group.Select(x => x.WaitType).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())}.",
                DatabaseName = databases.Length == 1 ? databases[0] : null,
                ConfidenceScore = 100m,
                ImpactScore = impact,
                FindingScore = impact,
                Fingerprint = $"BLK-002:{serverProfileId}:{group.Key}",
                FirstDetectedAt = capturedAt,
                LastDetectedAt = capturedAt
            });
        }

        return Task.FromResult<IReadOnlyCollection<Finding>>(findings);
    }

    private static bool TryClassifyWait(
        string waitType,
        long deltaWaitMs,
        out FindingSeverity severity,
        out decimal impact,
        out string label)
    {
        severity = FindingSeverity.Information;
        impact = 0m;
        label = string.Empty;

        long mediumThreshold;
        long highThreshold;
        long criticalThreshold;

        if (waitType.Equals("THREADPOOL", StringComparison.OrdinalIgnoreCase))
        {
            mediumThreshold = 1_000; highThreshold = 5_000; criticalThreshold = 15_000;
            label = "Worker thread baskısı";
        }
        else if (waitType.Equals("RESOURCE_SEMAPHORE", StringComparison.OrdinalIgnoreCase))
        {
            mediumThreshold = 2_000; highThreshold = 8_000; criticalThreshold = 20_000;
            label = "Memory grant baskısı";
        }
        else if (waitType.StartsWith("LCK_M_", StringComparison.OrdinalIgnoreCase))
        {
            mediumThreshold = 3_000; highThreshold = 10_000; criticalThreshold = 30_000;
            label = "Lock bekleme baskısı";
        }
        else if (waitType.StartsWith("PAGEIOLATCH_", StringComparison.OrdinalIgnoreCase))
        {
            mediumThreshold = 10_000; highThreshold = 30_000; criticalThreshold = 90_000;
            label = "Data file I/O bekleme baskısı";
        }
        else if (waitType.Equals("WRITELOG", StringComparison.OrdinalIgnoreCase))
        {
            mediumThreshold = 5_000; highThreshold = 20_000; criticalThreshold = 60_000;
            label = "Transaction log I/O baskısı";
        }
        else if (waitType.Equals("SOS_SCHEDULER_YIELD", StringComparison.OrdinalIgnoreCase))
        {
            mediumThreshold = 10_000; highThreshold = 30_000; criticalThreshold = 90_000;
            label = "CPU scheduler baskısı";
        }
        else
        {
            return false;
        }

        if (deltaWaitMs < mediumThreshold)
            return false;

        severity = deltaWaitMs >= criticalThreshold ? FindingSeverity.Critical
            : deltaWaitMs >= highThreshold ? FindingSeverity.High
            : FindingSeverity.Medium;

        impact = severity switch
        {
            FindingSeverity.Critical => 95m,
            FindingSeverity.High => 80m,
            _ => 60m
        };
        return true;
    }
}
