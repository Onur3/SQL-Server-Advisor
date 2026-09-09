using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Application.Presentation;
using SqlServerAdvisor.Infrastructure.Data;

namespace SqlServerAdvisor.Api.Controllers;

[ApiController]
[Route("api/queries")]
public sealed class QueriesController(AdvisorDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<QueryPerformanceDto>>> GetQueries(
        [FromQuery] Guid? serverId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var runtimeQuery = db.QueryRuntimeSnapshots.AsNoTracking().AsQueryable();
        if (serverId.HasValue)
            runtimeQuery = runtimeQuery.Where(x => x.ServerProfileId == serverId.Value);

        var recent = await runtimeQuery
            .OrderByDescending(x => x.CapturedAt)
            .ThenByDescending(x => x.ImpactScore)
            .Take(take * 8)
            .ToListAsync(cancellationToken);

        var latest = recent
            .GroupBy(x => x.QueryId)
            .Select(x => x.OrderByDescending(y => y.CapturedAt).First())
            .OrderByDescending(x => x.ImpactScore)
            .ThenByDescending(x => x.TotalCpuMs)
            .Take(take)
            .ToList();

        if (latest.Count == 0)
            return Ok(Array.Empty<QueryPerformanceDto>());

        var queryIds = latest.Select(x => x.QueryId).Distinct().ToArray();
        var serverIds = latest.Select(x => x.ServerProfileId).Distinct().ToArray();
        var planIds = latest.Where(x => x.PlanId.HasValue).Select(x => x.PlanId!.Value).Distinct().ToArray();

        var definitions = await db.Queries.AsNoTracking()
            .Where(x => queryIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var servers = await db.Servers.AsNoTracking()
            .Where(x => serverIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var plans = planIds.Length == 0
            ? new Dictionary<long, SqlServerAdvisor.Domain.Entities.QueryPlan>()
            : await db.QueryPlans.AsNoTracking()
                .Where(x => planIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var result = latest
            .Where(x => definitions.ContainsKey(x.QueryId))
            .Select(runtime =>
            {
                var query = definitions[runtime.QueryId];
                plans.TryGetValue(runtime.PlanId ?? 0, out var plan);
                servers.TryGetValue(runtime.ServerProfileId, out var serverName);
                var interpretation = QueryInterpretation.Build(
                    runtime.ExecutionCount,
                    runtime.AverageCpuMs,
                    runtime.AverageDurationMs,
                    runtime.AverageLogicalReads,
                    runtime.ImpactScore);

                return new QueryPerformanceDto(
                    query.Id,
                    runtime.ServerProfileId,
                    serverName ?? runtime.ServerProfileId.ToString(),
                    query.DatabaseName,
                    query.QueryHash,
                    query.ObjectName,
                    query.StatementText,
                    runtime.Source,
                    runtime.ExecutionCount,
                    runtime.TotalCpuMs,
                    runtime.AverageCpuMs,
                    runtime.TotalDurationMs,
                    runtime.AverageDurationMs,
                    runtime.TotalLogicalReads,
                    runtime.AverageLogicalReads,
                    runtime.TotalLogicalWrites,
                    runtime.ImpactScore,
                    interpretation.Level,
                    interpretation.Headline,
                    interpretation.Summary,
                    interpretation.SuggestedInspection,
                    runtime.LastExecutionTime,
                    runtime.CapturedAt,
                    runtime.PlanId,
                    plan?.PlanHash);
            })
            .ToList();

        return Ok(result);
    }

    [HttpGet("{queryId:long}/plan")]
    public async Task<ActionResult<QueryPlanDto>> GetLatestPlan(
        long queryId,
        CancellationToken cancellationToken = default)
    {
        var plan = await db.QueryPlans.AsNoTracking()
            .Where(x => x.QueryId == queryId)
            .OrderByDescending(x => x.LastSeenAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
            return NotFound();

        return Ok(new QueryPlanDto(
            plan.QueryId,
            plan.Id,
            plan.PlanHash,
            plan.Source,
            plan.HasActualRuntimeCounters,
            plan.PlanXml,
            plan.FirstSeenAt,
            plan.LastSeenAt));
    }
}
