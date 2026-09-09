using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Infrastructure.Data;

namespace SqlServerAdvisor.Worker.Workers;

public sealed class TelemetryWorker(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IAdvisorCollector> collectors,
    IEnumerable<ITelemetryAnalysisRule> rules,
    IRecommendationFactory recommendationFactory,
    ILogger<TelemetryWorker> logger) : BackgroundService
{
    private readonly Dictionary<string, DateTimeOffset> _nextRuns = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Telemetry collector başladı.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Telemetry collector cycle hatası.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task CollectCycleAsync(CancellationToken cancellationToken)
    {
        List<Guid> serverIds;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdvisorDbContext>();
            serverIds = await db.Servers.AsNoTracking()
                .Where(x => x.IsEnabled)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var serverId in serverIds)
        {
            foreach (var collector in collectors)
            {
                if (cancellationToken.IsCancellationRequested) return;

                var key = $"{serverId:N}:{collector.Name}";
                if (_nextRuns.TryGetValue(key, out var nextRun) && nextRun > now)
                    continue;

                _nextRuns[key] = now.AddSeconds(Math.Max(5, collector.DefaultIntervalSeconds));
                await CollectServerAsync(serverId, collector, cancellationToken);
            }
        }
    }

    private async Task CollectServerAsync(
        Guid serverId,
        IAdvisorCollector collector,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdvisorDbContext>();
        var server = await db.Servers.FirstOrDefaultAsync(x => x.Id == serverId, cancellationToken);
        if (server is null || !server.IsEnabled) return;

        var run = new CollectorRun
        {
            ServerProfileId = server.Id,
            CollectorType = collector.Name,
            StartedAt = DateTimeOffset.UtcNow,
            Status = "Running"
        };
        db.CollectorRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var batch = await collector.CollectAsync(server, cancellationToken);
            var capturedAt = DateTimeOffset.UtcNow;
            var waitSnapshots = await BuildWaitSnapshotsAsync(db, server.Id, batch.Waits, capturedAt, cancellationToken);

            if (waitSnapshots.Count > 0)
                db.WaitSnapshots.AddRange(waitSnapshots);
            if (batch.Blocking.Count > 0)
                db.BlockingEvents.AddRange(batch.Blocking);

            var activeFindings = new List<Finding>();
            var activeFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ruleIds = rules.SelectMany(x => x.RuleIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            foreach (var rule in rules)
            {
                var findings = await rule.EvaluateAsync(
                    server.Id,
                    capturedAt,
                    waitSnapshots,
                    batch.Blocking,
                    cancellationToken);

                foreach (var finding in findings)
                {
                    var tracked = await UpsertFindingAsync(db, finding, cancellationToken);
                    activeFindings.Add(tracked);
                    activeFingerprints.Add(tracked.Fingerprint);
                }
            }

            await ResolveInactiveFindingsAsync(
                db,
                server.Id,
                ruleIds,
                activeFingerprints,
                capturedAt,
                cancellationToken);

            await db.SaveChangesAsync(cancellationToken);

            foreach (var finding in activeFindings.DistinctBy(x => x.Id))
            {
                var recommendationExists = await db.Recommendations
                    .AnyAsync(x => x.FindingId == finding.Id, cancellationToken);
                if (recommendationExists) continue;

                var recommendation = recommendationFactory.Create(finding);
                if (recommendation is null) continue;

                recommendation.FindingId = finding.Id;
                recommendation.CanExecute = false;
                db.Recommendations.Add(recommendation);
            }

            run.Status = "Success";
            run.RowsCollected = waitSnapshots.Count + batch.Blocking.Count;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.DurationMs = (long)(run.CompletedAt.Value - run.StartedAt).TotalMilliseconds;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            run.Status = "Failed";
            run.ErrorMessage = ex.Message.Length > 4000 ? ex.Message[..4000] : ex.Message;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.DurationMs = (long)(run.CompletedAt.Value - run.StartedAt).TotalMilliseconds;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning(ex, "{CollectorName} collector {ServerName} için başarısız.", collector.Name, server.Name);
        }
    }

    private static async Task<List<WaitSnapshot>> BuildWaitSnapshotsAsync(
        AdvisorDbContext db,
        Guid serverId,
        IReadOnlyList<SqlServerAdvisor.Application.DTOs.WaitObservation> observations,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        if (observations.Count == 0) return [];

        var previousCapturedAt = await db.WaitSnapshots.AsNoTracking()
            .Where(x => x.ServerProfileId == serverId)
            .MaxAsync(x => (DateTimeOffset?)x.CapturedAt, cancellationToken);

        Dictionary<string, WaitSnapshot> previous = new(StringComparer.OrdinalIgnoreCase);
        if (previousCapturedAt.HasValue)
        {
            previous = await db.WaitSnapshots.AsNoTracking()
                .Where(x => x.ServerProfileId == serverId && x.CapturedAt == previousCapturedAt.Value)
                .ToDictionaryAsync(x => x.WaitType, StringComparer.OrdinalIgnoreCase, cancellationToken);
        }

        var result = new List<WaitSnapshot>(observations.Count);
        foreach (var observation in observations)
        {
            var deltaWait = 0L;
            var deltaSignal = 0L;

            if (previous.TryGetValue(observation.WaitType, out var old))
            {
                if (observation.WaitTimeMs >= old.WaitTimeMs)
                    deltaWait = observation.WaitTimeMs - old.WaitTimeMs;
                if (observation.SignalWaitTimeMs >= old.SignalWaitTimeMs)
                    deltaSignal = observation.SignalWaitTimeMs - old.SignalWaitTimeMs;
            }

            result.Add(new WaitSnapshot
            {
                ServerProfileId = serverId,
                WaitType = observation.WaitType,
                WaitingTasks = observation.WaitingTasks,
                WaitTimeMs = observation.WaitTimeMs,
                SignalWaitTimeMs = observation.SignalWaitTimeMs,
                DeltaWaitTimeMs = deltaWait,
                DeltaSignalWaitTimeMs = deltaSignal,
                CapturedAt = capturedAt
            });
        }

        return result;
    }

    private static async Task<Finding> UpsertFindingAsync(
        AdvisorDbContext db,
        Finding finding,
        CancellationToken cancellationToken)
    {
        var existing = await db.Findings
            .FirstOrDefaultAsync(x => x.ServerProfileId == finding.ServerProfileId &&
                                      x.Fingerprint == finding.Fingerprint &&
                                      x.Status == "Open", cancellationToken);
        if (existing is null)
        {
            db.Findings.Add(finding);
            return finding;
        }

        existing.LastDetectedAt = finding.LastDetectedAt;
        existing.OccurrenceCount++;
        existing.Severity = finding.Severity;
        existing.ImpactScore = finding.ImpactScore;
        existing.ConfidenceScore = finding.ConfidenceScore;
        existing.FindingScore = finding.FindingScore;
        existing.Title = finding.Title;
        existing.TechnicalDescription = finding.TechnicalDescription;
        existing.DatabaseName = finding.DatabaseName;
        existing.ObjectName = finding.ObjectName;
        existing.ResolvedAt = null;
        return existing;
    }

    private static async Task ResolveInactiveFindingsAsync(
        AdvisorDbContext db,
        Guid serverId,
        string[] ruleIds,
        HashSet<string> activeFingerprints,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        if (ruleIds.Length == 0) return;

        var openFindings = await db.Findings
            .Where(x => x.ServerProfileId == serverId &&
                        x.Status == "Open" &&
                        ruleIds.Contains(x.RuleId))
            .ToListAsync(cancellationToken);

        var resolvedIds = new List<long>();
        foreach (var finding in openFindings)
        {
            if (activeFingerprints.Contains(finding.Fingerprint)) continue;
            finding.Status = "Resolved";
            finding.ResolvedAt = capturedAt;
            resolvedIds.Add(finding.Id);
        }

        if (resolvedIds.Count == 0) return;

        var recommendations = await db.Recommendations
            .Where(x => resolvedIds.Contains(x.FindingId) && x.Status == "New")
            .ToListAsync(cancellationToken);

        foreach (var recommendation in recommendations)
            recommendation.Status = "Resolved";
    }
}
