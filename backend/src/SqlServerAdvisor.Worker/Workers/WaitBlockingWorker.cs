using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Analysis.Rules;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Infrastructure.Data;
using SqlServerAdvisor.SqlServer.Collectors;
namespace SqlServerAdvisor.Worker.Workers;

public sealed class WaitBlockingWorker(IServiceScopeFactory scopes, ILogger<WaitBlockingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AdvisorDbContext>();
                var ids = await db.Servers.AsNoTracking().Where(x => x.IsEnabled).Select(x => x.Id).ToListAsync(ct);
                foreach (var id in ids)
                    foreach (var type in new[] { "WaitStats", "BlockingDetail" })
                        await CollectAsync(id, type, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Wait/blocking cycle failed"); }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    private async Task CollectAsync(Guid id, string type, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdvisorDbContext>();
        var server = await db.Servers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsEnabled, ct);
        if (server is null) return;
        var setting = await db.CollectorSettings.AsNoTracking().SingleOrDefaultAsync(x => x.ServerProfileId == id && x.CollectorType == type, ct);
        if (setting is { IsEnabled: false }) return;
        var interval = Math.Clamp(setting?.IntervalSeconds ?? (type == "WaitStats" ? 60 : 15), 5, 3600);
        var timeout = Math.Clamp(setting?.TimeoutSeconds ?? 5, 1, 30);
        var last = await db.CollectorRuns.Where(x => x.ServerProfileId == id && x.CollectorType == type)
            .MaxAsync(x => (DateTimeOffset?)x.StartedAt, ct);
        if (last.HasValue && DateTimeOffset.UtcNow - last.Value < TimeSpan.FromSeconds(interval)) return;
        var run = new CollectorRun { ServerProfileId = id, CollectorType = type, StartedAt = DateTimeOffset.UtcNow, Status = "Running" };
        db.CollectorRuns.Add(run);
        await db.SaveChangesAsync(ct);
        try
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                run = await db.CollectorRuns.SingleAsync(x => x.Id == run.Id, ct);
                if (run.Status == "Success") return; // Commit acknowledgement may have been lost.
                var collector = scope.ServiceProvider.GetRequiredService<WaitBlockingCollector>();
                List<Finding> findings;
                var evaluate = true;
                // Collection writes and findings are atomic in the advisor database only.
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                if (type == "WaitStats")
                {
                    var rows = await collector.WaitsAsync(server, timeout, ct);
                    if (rows.Count == 0) throw new InvalidOperationException("Wait DMV returned no rows; baseline unavailable.");
                    var captured = await db.WaitSnapshots.Where(x => x.ServerProfileId == id).MaxAsync(x => (DateTimeOffset?)x.CapturedAt, ct);
                    var previous = await db.WaitSnapshots.AsNoTracking().Where(x => x.ServerProfileId == id && x.CapturedAt == captured).ToListAsync(ct);
                    evaluate = WaitBlockingAnalysis.ApplyDeltas(rows, previous, Math.Max(300000L, interval * 3000L));
                    findings = WaitBlockingAnalysis.Waits(rows);
                    db.WaitSnapshots.AddRange(rows);
                    run.RowsCollected = rows.Count;
                }
                else
                {
                    var rows = await collector.BlockingAsync(server, timeout, ct);
                    db.BlockingEvents.AddRange(rows);
                    findings = WaitBlockingAnalysis.Blocking(id, rows, run.StartedAt);
                    run.RowsCollected = rows.Count;
                }
                var tracked = new List<Finding>();
                foreach (var finding in findings)
                    tracked.Add(await ServerSnapshotWorker.UpsertFindingAsync(db, finding, ct));
                if (evaluate)
                    await ServerSnapshotWorker.ResolveInactiveFindingsAsync(db, id,
                        [type == "WaitStats" ? "WAIT-001" : "BLK-002"], findings.Select(x => x.Fingerprint).ToHashSet(StringComparer.OrdinalIgnoreCase), run.StartedAt, ct);
                await db.SaveChangesAsync(ct);
                var factory = scope.ServiceProvider.GetRequiredService<IRecommendationFactory>();
                foreach (var finding in tracked)
                    if (!await db.Recommendations.AnyAsync(x => x.FindingId == finding.Id, ct) && factory.Create(finding) is { } recommendation)
                    { recommendation.CanExecute = false; db.Recommendations.Add(recommendation); }
                run.Status = "Success";
                run.CompletedAt = DateTimeOffset.UtcNow;
                run.DurationMs = (long)(run.CompletedAt.Value - run.StartedAt).TotalMilliseconds;
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Use clean tracking after rollback: never accidentally save partial telemetry on failure.
            db.ChangeTracker.Clear();
            var failed = await db.CollectorRuns.SingleAsync(x => x.Id == run.Id, ct);
            failed.Status = "Failed";
            failed.ErrorMessage = ex.Message[..Math.Min(ex.Message.Length, 4000)];
            failed.CompletedAt = DateTimeOffset.UtcNow;
            failed.DurationMs = (long)(failed.CompletedAt.Value - failed.StartedAt).TotalMilliseconds;
            await db.SaveChangesAsync(ct);
            logger.LogWarning(ex, "{Collector} failed for {ServerId}", type, id);
        }
    }
}
