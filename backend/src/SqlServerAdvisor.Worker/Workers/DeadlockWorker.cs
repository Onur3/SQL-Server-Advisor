using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Infrastructure.Data;
using SqlServerAdvisor.SqlServer.Collectors;

namespace SqlServerAdvisor.Worker.Workers;

public sealed class DeadlockWorker(
    IServiceScopeFactory scopeFactory,
    DeadlockCollector collector,
    IEnumerable<IDeadlockAnalysisRule> rules,
    IRecommendationFactory recommendationFactory,
    ILogger<DeadlockWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Deadlock collector başladı.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                await CollectCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Deadlock collector cycle hatası.");
            }

            var delay = TimeSpan.FromSeconds(collector.DefaultIntervalSeconds) - (DateTimeOffset.UtcNow - startedAt);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, stoppingToken);
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

        foreach (var serverId in serverIds)
        {
            if (cancellationToken.IsCancellationRequested) break;
            await CollectServerAsync(serverId, cancellationToken);
        }
    }

    private async Task CollectServerAsync(Guid serverId, CancellationToken cancellationToken)
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
            var newEvents = new List<DeadlockEvent>();

            foreach (var deadlock in batch.Deadlocks)
            {
                var exists = await db.DeadlockEvents.AsNoTracking().AnyAsync(
                    x => x.ServerProfileId == server.Id &&
                         x.EventTime == deadlock.EventTime &&
                         x.Fingerprint == deadlock.Fingerprint,
                    cancellationToken);

                if (exists) continue;
                db.DeadlockEvents.Add(deadlock);
                newEvents.Add(deadlock);
            }

            await db.SaveChangesAsync(cancellationToken);

            var activeFindings = new List<Finding>();
            foreach (var rule in rules)
            {
                var findings = await rule.EvaluateAsync(
                    server.Id,
                    capturedAt,
                    newEvents,
                    cancellationToken);

                foreach (var finding in findings)
                    activeFindings.Add(await UpsertFindingAsync(db, finding, cancellationToken));
            }

            await ResolveStaleFindingsAsync(db, server.Id, capturedAt, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            foreach (var finding in activeFindings.DistinctBy(x => x.Id))
            {
                var recommendationExists = await db.Recommendations.AsNoTracking()
                    .AnyAsync(x => x.FindingId == finding.Id, cancellationToken);
                if (recommendationExists) continue;

                var recommendation = recommendationFactory.Create(finding);
                if (recommendation is null) continue;

                recommendation.FindingId = finding.Id;
                recommendation.CanExecute = false;
                db.Recommendations.Add(recommendation);
            }

            run.Status = "Success";
            run.RowsCollected = newEvents.Count;
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
            logger.LogWarning(ex, "Deadlock collector {ServerName} için başarısız.", server.Name);
        }
    }

    private static async Task<Finding> UpsertFindingAsync(
        AdvisorDbContext db,
        Finding finding,
        CancellationToken cancellationToken)
    {
        var existing = await db.Findings.FirstOrDefaultAsync(
            x => x.ServerProfileId == finding.ServerProfileId &&
                 x.Fingerprint == finding.Fingerprint &&
                 x.Status == "Open",
            cancellationToken);

        if (existing is null)
        {
            db.Findings.Add(finding);
            return finding;
        }

        existing.LastDetectedAt = finding.LastDetectedAt;
        existing.OccurrenceCount += Math.Max(1, finding.OccurrenceCount);
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

    private static async Task ResolveStaleFindingsAsync(
        AdvisorDbContext db,
        Guid serverId,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        var cutoff = capturedAt.AddMinutes(-30);
        var stale = await db.Findings
            .Where(x => x.ServerProfileId == serverId &&
                        x.RuleId == "DLK-001" &&
                        x.Status == "Open" &&
                        x.LastDetectedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0) return;

        var ids = stale.Select(x => x.Id).ToArray();
        foreach (var finding in stale)
        {
            finding.Status = "Resolved";
            finding.ResolvedAt = capturedAt;
        }

        var recommendations = await db.Recommendations
            .Where(x => ids.Contains(x.FindingId) && x.Status == "New")
            .ToListAsync(cancellationToken);

        foreach (var recommendation in recommendations)
            recommendation.Status = "Resolved";
    }
}
