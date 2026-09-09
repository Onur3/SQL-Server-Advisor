using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Infrastructure.Data;
namespace SqlServerAdvisor.Api.Controllers;

[ApiController]
[Route("api/telemetry")]
public sealed class TelemetryController(AdvisorDbContext db) : ControllerBase
{
    [HttpGet("{serverId:guid}/{kind}")]
    public async Task<IActionResult> Get(Guid serverId, string kind, CancellationToken ct)
    {
        if (kind is not ("waits" or "blocking")) return BadRequest("kind must be waits or blocking");
        if (!await db.Servers.AnyAsync(x => x.Id == serverId, ct)) return NotFound();
        var type = kind == "waits" ? "WaitStats" : "BlockingDetail";
        var latest = await db.CollectorRuns.AsNoTracking().Where(x => x.ServerProfileId == serverId && x.CollectorType == type)
            .OrderByDescending(x => x.StartedAt).Select(x => new { x.Status, x.StartedAt, x.CompletedAt, x.ErrorMessage }).FirstOrDefaultAsync(ct);
        var success = await db.CollectorRuns.AsNoTracking().Where(x => x.ServerProfileId == serverId && x.CollectorType == type && x.Status == "Success")
            .OrderByDescending(x => x.StartedAt).Select(x => new { x.StartedAt, x.CompletedAt, x.RowsCollected }).FirstOrDefaultAsync(ct);
        var setting = await db.CollectorSettings.AsNoTracking().Where(x => x.ServerProfileId == serverId && x.CollectorType == type)
            .Select(x => new { x.IsEnabled, x.IntervalSeconds }).SingleOrDefaultAsync(ct);
        var enabled = setting?.IsEnabled ?? true;
        var interval = Math.Clamp(setting?.IntervalSeconds ?? (kind == "waits" ? 60 : 15), 5, 3600);
        var stale = success?.CompletedAt is null || DateTimeOffset.UtcNow - success.CompletedAt.Value > TimeSpan.FromSeconds(interval * 3);
        var start = success?.StartedAt;
        var end = success?.CompletedAt;
        if (kind == "waits")
        {
            var rows = await db.WaitSnapshots.AsNoTracking().Where(x => x.ServerProfileId == serverId && x.CapturedAt >= start && x.CapturedAt <= end)
                .OrderByDescending(x => x.DeltaWaitTimeMs).ThenBy(x => x.WaitType).Take(2000).ToListAsync(ct);
            return Ok(new { latest, success, enabled, stale, rows });
        }
        var blocking = await db.BlockingEvents.AsNoTracking().Where(x => x.ServerProfileId == serverId && x.CapturedAt >= start && x.CapturedAt <= end)
            .OrderByDescending(x => x.WaitTimeMs).ThenBy(x => x.SessionId).Take(1000).ToListAsync(ct);
        return Ok(new { latest, success, enabled, stale, rows = blocking });
    }
}
