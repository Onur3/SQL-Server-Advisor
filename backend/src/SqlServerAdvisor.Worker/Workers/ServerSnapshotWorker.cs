using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Infrastructure.Data;

namespace SqlServerAdvisor.Worker.Workers;

public sealed class ServerSnapshotWorker(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IAnalysisRule> rules,
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

        var run = new SqlServerAdvisor.Domain.Entities.CollectorRun
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

            foreach (var rule in rules)
            {
                var findings = await rule.EvaluateAsync(snapshot, cancellationToken);
                foreach (var finding in findings)
                    await UpsertFindingAsync(db, finding, cancellationToken);
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

    private static async Task UpsertFindingAsync(AdvisorDbContext db, SqlServerAdvisor.Domain.Entities.Finding finding, CancellationToken cancellationToken)
    {
        var existing = await db.Findings
            .FirstOrDefaultAsync(x => x.ServerProfileId == finding.ServerProfileId &&
                                      x.Fingerprint == finding.Fingerprint &&
                                      x.Status == "Open", cancellationToken);
        if (existing is null)
        {
            db.Findings.Add(finding);
            return;
        }

        existing.LastDetectedAt = finding.LastDetectedAt;
        existing.OccurrenceCount++;
        existing.Severity = finding.Severity;
        existing.ImpactScore = finding.ImpactScore;
        existing.ConfidenceScore = finding.ConfidenceScore;
        existing.FindingScore = finding.FindingScore;
        existing.TechnicalDescription = finding.TechnicalDescription;
    }
}
