using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Application.Presentation;
using SqlServerAdvisor.Infrastructure.Data;

namespace SqlServerAdvisor.Api.Controllers;

[ApiController]
[Route("api/statistics")]
public sealed class StatisticsController(AdvisorDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<StatisticsStatusDto>>> GetStatistics(
        [FromQuery] Guid? serverId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var query = db.StatisticsSnapshots.AsNoTracking().AsQueryable();
        if (serverId.HasValue)
            query = query.Where(x => x.ServerProfileId == serverId.Value);

        var recent = await query
            .Where(x => x.Rows >= 1000)
            .OrderByDescending(x => x.CapturedAt)
            .ThenByDescending(x => x.ModificationCounter)
            .Take(take * 10)
            .ToListAsync(cancellationToken);

        var latest = recent
            .GroupBy(x => new { x.ServerProfileId, x.DatabaseName, x.ObjectId, x.StatisticsId })
            .Select(x => x.OrderByDescending(y => y.CapturedAt).First())
            .OrderByDescending(x => x.Rows == 0 ? 0m : x.ModificationCounter * 100m / x.Rows)
            .ThenByDescending(x => x.ModificationCounter)
            .Take(take)
            .ToList();

        var serverIds = latest.Select(x => x.ServerProfileId).Distinct().ToArray();
        var servers = await db.Servers.AsNoTracking()
            .Where(x => serverIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        return Ok(latest.Select(x =>
        {
            var modificationPercent = x.Rows == 0 ? 0m : Math.Round(x.ModificationCounter * 100m / x.Rows, 2);
            var interpretation = StatisticsInterpretation.Build(
                x.Rows,
                x.RowsSampled,
                modificationPercent,
                x.LastUpdated,
                x.SamplePercent,
                now);

            return new StatisticsStatusDto(
                x.Id,
                x.ServerProfileId,
                servers.GetValueOrDefault(x.ServerProfileId, x.ServerProfileId.ToString()),
                x.DatabaseName,
                x.TableName,
                x.StatisticsName,
                x.Rows,
                x.RowsSampled,
                x.ModificationCounter,
                modificationPercent,
                x.LastUpdated,
                x.SamplePercent,
                interpretation.Level,
                interpretation.Headline,
                interpretation.Summary,
                interpretation.SuggestedInspection,
                x.CapturedAt);
        }).ToList());
    }
}
