using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Infrastructure.Data;

namespace SqlServerAdvisor.Api.Controllers;

[ApiController]
[Route("api/diagnostics")]
public sealed class DiagnosticsController(AdvisorDbContext db) : ControllerBase
{
    [HttpGet("collector-coverage")]
    public async Task<ActionResult<IReadOnlyCollection<CollectorCoverageDto>>> GetCollectorCoverage(
        [FromQuery] string collectorType,
        [FromQuery] Guid? serverId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(collectorType))
            return BadRequest("collectorType zorunludur.");

        var runs = db.CollectorRuns.AsNoTracking()
            .Where(x => x.CollectorType == collectorType);

        if (serverId.HasValue)
            runs = runs.Where(x => x.ServerProfileId == serverId.Value);

        var recent = await runs
            .OrderByDescending(x => x.StartedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        var latest = recent
            .GroupBy(x => x.ServerProfileId)
            .Select(g => g.OrderByDescending(x => x.StartedAt).First())
            .ToList();

        var serverIds = latest.Select(x => x.ServerProfileId).Distinct().ToArray();
        var serverNames = await db.Servers.AsNoTracking()
            .Where(x => serverIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return Ok(latest.Select(x => new CollectorCoverageDto(
            x.ServerProfileId,
            serverNames.GetValueOrDefault(x.ServerProfileId, x.ServerProfileId.ToString()),
            x.CollectorType,
            x.Status,
            x.RowsCollected,
            x.ErrorMessage,
            x.StartedAt,
            x.CompletedAt)).ToList());
    }
}
