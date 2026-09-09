using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Analysis.Scoring;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Infrastructure.Data;

namespace SqlServerAdvisor.Worker.Workers;

public sealed class TelemetryWorker(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IAdvisorCollector> collectors,
    IEnumerable<ITelemetryAnalysisRule> telemetryRules,
    IEnumerable<IQueryAnalysisRule> queryRules,
    IEnumerable<IIndexAnalysisRule> indexRules,
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
            var queryContexts = await PersistQueriesAsync(db, server.Id, batch.Queries, capturedAt, cancellationToken);

            if (waitSnapshots.Count > 0)
                db.WaitSnapshots.AddRange(waitSnapshots);
            if (batch.Blocking.Count > 0)
                db.BlockingEvents.AddRange(batch.Blocking);
            if (batch.Indexes.Count > 0)
                db.IndexSnapshots.AddRange(batch.Indexes);
            if (batch.MissingIndexes.Count > 0)
                db.MissingIndexSnapshots.AddRange(batch.MissingIndexes);

            var activeFindings = new List<Finding>();

            if (collector.Name.Equals("WaitBlocking", StringComparison.OrdinalIgnoreCase) ||
                waitSnapshots.Count > 0 || batch.Blocking.Count > 0)
            {
                var activeFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var ruleIds = telemetryRules.SelectMany(x => x.RuleIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var rule in telemetryRules)
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
            }

            if (collector.Name.Equals("QueryPerformance", StringComparison.OrdinalIgnoreCase) ||
                queryContexts.Count > 0)
            {
                var activeFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var ruleIds = queryRules.SelectMany(x => x.RuleIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var context in queryContexts)
                {
                    foreach (var rule in queryRules)
                    {
                        var findings = await rule.EvaluateAsync(
                            server.Id,
                            capturedAt,
                            context.Query,
                            context.Runtime,
                            cancellationToken);

                        foreach (var finding in findings)
                        {
                            var tracked = await UpsertFindingAsync(db, finding, cancellationToken);
                            activeFindings.Add(tracked);
                            activeFingerprints.Add(tracked.Fingerprint);
                        }
                    }
                }

                await ResolveInactiveFindingsAsync(
                    db,
                    server.Id,
                    ruleIds,
                    activeFingerprints,
                    capturedAt,
                    cancellationToken);
            }

            if (collector.Name.Equals("IndexAdvisor", StringComparison.OrdinalIgnoreCase) ||
                batch.Indexes.Count > 0 || batch.MissingIndexes.Count > 0)
            {
                var activeFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var ruleIds = indexRules.SelectMany(x => x.RuleIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var rule in indexRules)
                {
                    var findings = await rule.EvaluateAsync(
                        server.Id,
                        capturedAt,
                        batch.Indexes,
                        batch.MissingIndexes,
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
            }

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
            run.RowsCollected = waitSnapshots.Count + batch.Blocking.Count + queryContexts.Count + batch.Indexes.Count + batch.MissingIndexes.Count;
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
        IReadOnlyList<WaitObservation> observations,
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

    private static async Task<List<QueryContext>> PersistQueriesAsync(
        AdvisorDbContext db,
        Guid serverId,
        IReadOnlyList<QueryObservation> observations,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        if (observations.Count == 0) return [];

        var hashes = observations.Select(x => x.QueryHash).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existingQueries = await db.Queries
            .Where(x => x.ServerProfileId == serverId && hashes.Contains(x.QueryHash))
            .ToListAsync(cancellationToken);

        var queryMap = existingQueries.ToDictionary(
            x => QueryKey(x.DatabaseName, x.QueryHash),
            StringComparer.OrdinalIgnoreCase);

        foreach (var observation in observations)
        {
            var key = QueryKey(observation.DatabaseName, observation.QueryHash);
            if (!queryMap.TryGetValue(key, out var query))
            {
                query = new QueryDefinition
                {
                    ServerProfileId = serverId,
                    DatabaseName = observation.DatabaseName,
                    QueryHash = observation.QueryHash,
                    NormalizedHash = string.IsNullOrWhiteSpace(observation.NormalizedHash) ? observation.QueryHash : observation.NormalizedHash,
                    ObjectId = observation.ObjectId,
                    ObjectName = observation.ObjectName,
                    StatementText = observation.StatementText,
                    FirstSeenAt = capturedAt,
                    LastSeenAt = capturedAt
                };
                db.Queries.Add(query);
                queryMap[key] = query;
            }
            else
            {
                query.LastSeenAt = capturedAt;
                query.ObjectId = observation.ObjectId;
                query.ObjectName = observation.ObjectName;
                query.StatementText = observation.StatementText;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var queryIds = queryMap.Values.Select(x => x.Id).Distinct().ToArray();
        var existingPlans = await db.QueryPlans
            .Where(x => queryIds.Contains(x.QueryId))
            .ToListAsync(cancellationToken);

        var planMap = existingPlans.ToDictionary(
            x => PlanKey(x.QueryId, x.PlanHash, x.Source),
            StringComparer.OrdinalIgnoreCase);

        foreach (var observation in observations)
        {
            if (string.IsNullOrWhiteSpace(observation.PlanHash)) continue;

            var query = queryMap[QueryKey(observation.DatabaseName, observation.QueryHash)];
            var source = string.IsNullOrWhiteSpace(observation.Source) ? "PlanCache" : observation.Source;
            var key = PlanKey(query.Id, observation.PlanHash, source);

            if (!planMap.TryGetValue(key, out var plan))
            {
                plan = new QueryPlan
                {
                    QueryId = query.Id,
                    PlanHash = observation.PlanHash,
                    Source = source,
                    HasActualRuntimeCounters = observation.HasActualRuntimeCounters,
                    PlanXml = observation.PlanXml ?? string.Empty,
                    FirstSeenAt = capturedAt,
                    LastSeenAt = capturedAt
                };
                db.QueryPlans.Add(plan);
                planMap[key] = plan;
            }
            else
            {
                plan.LastSeenAt = capturedAt;
                if (!string.IsNullOrWhiteSpace(observation.PlanXml))
                    plan.PlanXml = observation.PlanXml;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var result = new List<QueryContext>(observations.Count);
        foreach (var observation in observations)
        {
            var query = queryMap[QueryKey(observation.DatabaseName, observation.QueryHash)];
            QueryPlan? plan = null;
            var source = string.IsNullOrWhiteSpace(observation.Source) ? "PlanCache" : observation.Source;

            if (!string.IsNullOrWhiteSpace(observation.PlanHash))
                planMap.TryGetValue(PlanKey(query.Id, observation.PlanHash, source), out plan);

            var runtime = new QueryRuntimeSnapshot
            {
                ServerProfileId = serverId,
                QueryId = query.Id,
                PlanId = plan?.Id,
                Source = source,
                ExecutionCount = observation.ExecutionCount,
                TotalCpuMs = observation.TotalCpuMs,
                AverageCpuMs = observation.AverageCpuMs,
                TotalDurationMs = observation.TotalDurationMs,
                AverageDurationMs = observation.AverageDurationMs,
                TotalLogicalReads = observation.TotalLogicalReads,
                AverageLogicalReads = observation.AverageLogicalReads,
                TotalLogicalWrites = observation.TotalLogicalWrites,
                ImpactScore = QueryPerformanceScorer.Calculate(
                    observation.ExecutionCount,
                    observation.AverageCpuMs,
                    observation.AverageDurationMs,
                    observation.AverageLogicalReads),
                LastExecutionTime = observation.LastExecutionTime,
                CapturedAt = capturedAt
            };

            db.QueryRuntimeSnapshots.Add(runtime);
            result.Add(new QueryContext(query, runtime));
        }

        return result;
    }

    private static string QueryKey(string databaseName, string queryHash) => $"{databaseName}\u001f{queryHash}";
    private static string PlanKey(long queryId, string planHash, string source) => $"{queryId}\u001f{planHash}\u001f{source}";

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
        existing.QueryId = finding.QueryId;
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

    private sealed record QueryContext(QueryDefinition Query, QueryRuntimeSnapshot Runtime);
}
