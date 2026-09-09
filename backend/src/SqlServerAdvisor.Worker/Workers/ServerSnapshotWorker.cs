using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Infrastructure.Data;

namespace SqlServerAdvisor.Worker.Workers;

public sealed class ServerSnapshotWorker(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IAnalysisRule> rules,
    IRecommendationFactory recommendationFactory,
    ILogger<ServerSnapshotWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Server snapshot collector başladı.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var cycleStartedAt = DateTimeOffset.UtcNow;

            try
            {
                await CollectCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Server snapshot collector cycle hatası.");
            }

            var elapsed = DateTimeOffset.UtcNow - cycleStartedAt;
            var delay = TimeSpan.FromSeconds(15) - elapsed;
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
        var collector = scope.ServiceProvider.GetRequiredService<IServerHealthCollector>();

        var server = await db.Servers.FirstOrDefaultAsync(x => x.Id == serverId, cancellationToken);
        if (server is null || !server.IsEnabled) return;

        var run = new CollectorRun
        {
            ServerProfileId = server.Id,
            CollectorType = "ServerHealth",
            StartedAt = DateTimeOffset.UtcNow,
            Status = "Running"
        };
        db.CollectorRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var snapshot = await collector.CollectAsync(server, cancellationToken);
            db.ServerSnapshots.Add(snapshot);

            var activeFindings = new List<Finding>();
            var activeFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ruleIds = rules.Select(x => x.RuleId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            foreach (var rule in rules)
            {
                var findings = await rule.EvaluateAsync(snapshot, cancellationToken);
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
                snapshot.CapturedAt,
                cancellationToken);

            // Persist findings first so newly inserted rows receive identity values.
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

            server.LastConnectedAt = DateTimeOffset.UtcNow;
            server.LastError = null;
            run.Status = "Success";
            run.RowsCollected = 1;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.DurationMs = (long)(run.CompletedAt.Value - run.StartedAt).TotalMilliseconds;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            server.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            run.Status = "Failed";
            run.ErrorMessage = server.LastError;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.DurationMs = (long)(run.CompletedAt.Value - run.StartedAt).TotalMilliseconds;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning(ex, "{ServerName} snapshot alınamadı.", server.Name);
        }
    }

    internal static async Task<Finding> UpsertFindingAsync(
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
        existing.ResolvedAt = null;
        return existing;
    }

    internal static async Task ResolveInactiveFindingsAsync(
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
