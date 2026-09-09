using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Analysis.Scoring;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Infrastructure.Data;

namespace SqlServerAdvisor.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(AdvisorDbContext db) : ControllerBase
{
    [HttpGet("servers")]
    public async Task<ActionResult<IReadOnlyCollection<DashboardServerDto>>> GetServers(CancellationToken cancellationToken)
    {
        var servers = await db.Servers.AsNoTracking().Where(x => x.IsEnabled).OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var result = new List<DashboardServerDto>(servers.Count);

        foreach (var server in servers)
        {
            var snapshot = await db.ServerSnapshots.AsNoTracking()
                .Where(x => x.ServerProfileId == server.Id)
                .OrderByDescending(x => x.CapturedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var online = snapshot is not null && snapshot.CapturedAt >= DateTimeOffset.UtcNow.AddMinutes(-1);
            result.Add(new DashboardServerDto(
                server.Id,
                server.Name,
                server.Host,
                online,
                snapshot?.CapturedAt,
                snapshot?.SqlCpuPercent,
                snapshot?.AvailableMemoryMb,
                snapshot?.ActiveSessions ?? 0,
                snapshot?.ActiveRequests ?? 0,
                snapshot?.BlockedRequests ?? 0,
                snapshot is null ? null : ServerHealthScorer.Calculate(snapshot),
                snapshot is null ? 0 : ServerHealthScorer.DataCoveragePercent(snapshot)));
        }

        return Ok(result);
    }

    [HttpGet("worker")]
    public async Task<ActionResult<WorkerStatusDto>> GetWorker(CancellationToken cancellationToken)
    {
        var heartbeat = await db.WorkerHeartbeats.AsNoTracking()
            .OrderByDescending(x => x.LastHeartbeatAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (heartbeat is null)
            return Ok(new WorkerStatusDto(false, null, null, null, null));

        var online = heartbeat.LastHeartbeatAt >= DateTimeOffset.UtcNow.AddSeconds(-30);
        return Ok(new WorkerStatusDto(online, heartbeat.MachineName, heartbeat.Version, heartbeat.StartedAt, heartbeat.LastHeartbeatAt));
    }
}
