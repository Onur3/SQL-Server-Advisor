using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Infrastructure.Data;

namespace SqlServerAdvisor.Api.Controllers;

[ApiController]
[Route("api/telemetry")]
public sealed class TelemetryController(AdvisorDbContext db) : ControllerBase
{
    [HttpGet("waits")]
    public async Task<ActionResult<IReadOnlyCollection<WaitTelemetryDto>>> GetWaits(
        [FromQuery] Guid? serverId,
        [FromQuery] int minutes = 15,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        minutes = Math.Clamp(minutes, 1, 1440);
        take = Math.Clamp(take, 1, 500);
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-minutes);

        var query =
            from wait in db.WaitSnapshots.AsNoTracking()
            join server in db.Servers.AsNoTracking() on wait.ServerProfileId equals server.Id
            where wait.CapturedAt >= cutoff && wait.DeltaWaitTimeMs > 0
            select new { wait, server };

        if (serverId.HasValue)
            query = query.Where(x => x.wait.ServerProfileId == serverId.Value);

        var rows = await query
            .OrderByDescending(x => x.wait.CapturedAt)
            .ThenByDescending(x => x.wait.DeltaWaitTimeMs)
            .Take(take)
            .Select(x => new WaitTelemetryDto(
                x.wait.ServerProfileId,
                x.server.Name,
                x.wait.CapturedAt,
                x.wait.WaitType,
                x.wait.WaitingTasks,
                x.wait.WaitTimeMs,
                x.wait.DeltaWaitTimeMs,
                x.wait.DeltaSignalWaitTimeMs))
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpGet("blocking")]
    public async Task<ActionResult<IReadOnlyCollection<BlockingTelemetryDto>>> GetBlocking(
        [FromQuery] Guid? serverId,
        [FromQuery] int minutes = 15,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        minutes = Math.Clamp(minutes, 1, 1440);
        take = Math.Clamp(take, 1, 500);
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-minutes);

        var query =
            from blocking in db.BlockingEvents.AsNoTracking()
            join server in db.Servers.AsNoTracking() on blocking.ServerProfileId equals server.Id
            where blocking.CapturedAt >= cutoff
            select new { blocking, server };

        if (serverId.HasValue)
            query = query.Where(x => x.blocking.ServerProfileId == serverId.Value);

        var rows = await query
            .OrderByDescending(x => x.blocking.CapturedAt)
            .ThenByDescending(x => x.blocking.WaitTimeMs)
            .Take(take)
            .Select(x => new BlockingTelemetryDto(
                x.blocking.ServerProfileId,
                x.server.Name,
                x.blocking.CapturedAt,
                x.blocking.SessionId,
                x.blocking.BlockingSessionId,
                x.blocking.DatabaseName,
                x.blocking.WaitType,
                x.blocking.WaitTimeMs,
                x.blocking.WaitResource,
                x.blocking.SqlText,
                x.blocking.HostName,
                x.blocking.ProgramName,
                x.blocking.LoginName))
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }
}
