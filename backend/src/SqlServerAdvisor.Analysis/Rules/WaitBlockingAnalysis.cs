using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Domain.Enums;
namespace SqlServerAdvisor.Analysis.Rules;

public static class WaitBlockingAnalysis
{
    // Deliberate diagnostic allowlist: idle, system, parallel-consumer and unknown waits never alert.
    public static bool IsActionable(string type) => type.StartsWith("LCK_M_", StringComparison.Ordinal)
        || type.StartsWith("PAGEIOLATCH_", StringComparison.Ordinal)
        || type is "WRITELOG" or "RESOURCE_SEMAPHORE" or "THREADPOOL" or "SOS_SCHEDULER_YIELD";

    public static bool ApplyDeltas(List<WaitSnapshot> current, List<WaitSnapshot> previous, long maxGapMs = 300000)
    {
        var prior = previous.ToDictionary(x => x.WaitType);
        var interval = previous.Count == 0 || current.Count == 0 ? 0L
            : (long)(current[0].CapturedAt - previous[0].CapturedAt).TotalMilliseconds;
        var reset = interval <= 0 || interval > maxGapMs || previous.Count == 0 || current.Count == 0
            || current[0].SqlServerStartTime != previous[0].SqlServerStartTime
            || previous.Any(x => !current.Any(c => c.WaitType == x.WaitType))
            || current.Any(x => prior.TryGetValue(x.WaitType, out var p)
                && (x.WaitTimeMs < p.WaitTimeMs || x.SignalWaitTimeMs < p.SignalWaitTimeMs || x.WaitingTasks < p.WaitingTasks));
        foreach (var row in current)
        {
            row.IsBaseline = reset || !prior.ContainsKey(row.WaitType);
            row.IntervalMs = row.IsBaseline ? 0 : interval;
            row.DeltaWaitTimeMs = row.IsBaseline ? 0 : row.WaitTimeMs - prior[row.WaitType].WaitTimeMs;
            row.DeltaSignalWaitTimeMs = row.IsBaseline ? 0 : row.SignalWaitTimeMs - prior[row.WaitType].SignalWaitTimeMs;
        }
        return !reset;
    }

    public static List<Finding> Waits(List<WaitSnapshot> rows) => rows
        .Where(x => !x.IsBaseline && IsActionable(x.WaitType) && x.DeltaWaitTimeMs >= 5000
            && x.DeltaWaitTimeMs >= x.IntervalMs / 2)
        .Select(x => Create(x.ServerProfileId, "WAIT-001", "Waits", x.WaitType,
            $"{x.WaitType}: elevated interval waits",
            $"{x.DeltaWaitTimeMs} ms accumulated across tasks in {x.IntervalMs} ms; signal {x.DeltaSignalWaitTimeMs} ms. Correlate with workload before changing settings.", x.CapturedAt)).ToList();

    public static List<Finding> Blocking(Guid serverId, List<BlockingEvent> rows, DateTimeOffset now) =>
        rows.Any(x => x.WaitTimeMs >= 15000)
        ? [Create(serverId, "BLK-002", "Blocking", "long-blocking", "Blocking exceeds 15 seconds",
            $"{rows.Count(x => x.WaitTimeMs >= 15000)} requests waiting at least 15 seconds. Inspect blocking details and sleeping blockers; negative blocker IDs represent special owners.", now)] : [];

    private static Finding Create(Guid serverId, string rule, string category, string key, string title, string detail, DateTimeOffset now) => new()
    {
        ServerProfileId = serverId, RuleId = rule, Category = category, Fingerprint = $"{rule}:{key}",
        Title = title, TechnicalDescription = detail, Severity = FindingSeverity.Medium,
        ConfidenceScore = 75, ImpactScore = 60, FindingScore = 60, FirstDetectedAt = now, LastDetectedAt = now
    };
}
