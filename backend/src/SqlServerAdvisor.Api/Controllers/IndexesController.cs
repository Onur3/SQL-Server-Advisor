using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Infrastructure.Data;

namespace SqlServerAdvisor.Api.Controllers;

[ApiController]
[Route("api/indexes")]
public sealed class IndexesController(AdvisorDbContext db) : ControllerBase
{
    [HttpGet("fragmented")]
    public async Task<ActionResult<IReadOnlyCollection<FragmentedIndexDto>>> GetFragmented(
        [FromQuery] Guid? serverId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var query = db.IndexSnapshots.AsNoTracking()
            .Where(x => x.AvgFragmentationPercent != null && x.PageCount != null && x.PageCount >= 1000);

        if (serverId.HasValue)
            query = query.Where(x => x.ServerProfileId == serverId.Value);

        var recent = await query
            .OrderByDescending(x => x.CapturedAt)
            .ThenByDescending(x => x.AvgFragmentationPercent)
            .Take(take * 10)
            .ToListAsync(cancellationToken);

        var latest = recent
            .GroupBy(x => new { x.ServerProfileId, x.DatabaseName, x.ObjectId, x.IndexId })
            .Select(x => x.OrderByDescending(y => y.CapturedAt).First())
            .OrderByDescending(x => x.AvgFragmentationPercent)
            .ThenByDescending(x => x.PageCount)
            .Take(take)
            .ToList();

        var serverIds = latest.Select(x => x.ServerProfileId).Distinct().ToArray();
        var servers = await db.Servers.AsNoTracking()
            .Where(x => serverIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return Ok(latest.Select(x => new FragmentedIndexDto(
            x.Id,
            x.ServerProfileId,
            servers.GetValueOrDefault(x.ServerProfileId, x.ServerProfileId.ToString()),
            x.DatabaseName,
            x.TableName,
            x.IndexName,
            x.TypeDesc,
            x.KeyColumns,
            x.IncludeColumns,
            x.SizeMb,
            x.UserSeeks,
            x.UserScans,
            x.UserLookups,
            x.UserUpdates,
            x.AvgFragmentationPercent,
            x.PageCount,
            x.CapturedAt)).ToList());
    }

    [HttpGet("missing")]
    public async Task<ActionResult<IReadOnlyCollection<MissingIndexDto>>> GetMissing(
        [FromQuery] Guid? serverId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);

        var query = db.MissingIndexSnapshots.AsNoTracking().AsQueryable();
        if (serverId.HasValue)
            query = query.Where(x => x.ServerProfileId == serverId.Value);

        var recent = await query
            .OrderByDescending(x => x.CapturedAt)
            .ThenByDescending(x => x.ImprovementMeasure)
            .Take(take * 10)
            .ToListAsync(cancellationToken);

        var latest = recent
            .GroupBy(x => new { x.ServerProfileId, x.DatabaseName, x.TableName, x.EqualityColumns, x.InequalityColumns, x.IncludedColumns })
            .Select(x => x.OrderByDescending(y => y.CapturedAt).First())
            .OrderByDescending(x => x.ImprovementMeasure)
            .Take(take)
            .ToList();

        var serverIds = latest.Select(x => x.ServerProfileId).Distinct().ToArray();
        var servers = await db.Servers.AsNoTracking()
            .Where(x => serverIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return Ok(latest.Select(x => new MissingIndexDto(
            x.Id,
            x.ServerProfileId,
            servers.GetValueOrDefault(x.ServerProfileId, x.ServerProfileId.ToString()),
            x.DatabaseName,
            x.TableName,
            x.EqualityColumns,
            x.InequalityColumns,
            x.IncludedColumns,
            x.UserSeeks,
            x.UserScans,
            x.AvgTotalUserCost,
            x.AvgUserImpact,
            x.ImprovementMeasure,
            x.CapturedAt)).ToList());
    }
}
