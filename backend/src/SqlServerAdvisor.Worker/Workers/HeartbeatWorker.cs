using Microsoft.EntityFrameworkCore;
using SqlServerAdvisor.Domain.Entities;
using SqlServerAdvisor.Infrastructure.Data;

namespace SqlServerAdvisor.Worker.Workers;

public sealed class HeartbeatWorker(IServiceScopeFactory scopeFactory, ILogger<HeartbeatWorker> logger) : BackgroundService
{
    private readonly Guid _heartbeatId = Guid.NewGuid();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SQL Server Advisor heartbeat worker başladı.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AdvisorDbContext>();
                var heartbeat = await db.WorkerHeartbeats.FirstOrDefaultAsync(x => x.Id == _heartbeatId, stoppingToken);

                if (heartbeat is null)
                {
                    heartbeat = new WorkerHeartbeat
                    {
                        Id = _heartbeatId,
                        MachineName = Environment.MachineName,
                        ProcessId = Environment.ProcessId,
                        Version = typeof(HeartbeatWorker).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                        StartedAt = _startedAt,
                        LastHeartbeatAt = DateTimeOffset.UtcNow,
                        Status = "Online"
                    };
                    db.WorkerHeartbeats.Add(heartbeat);
                }
                else
                {
                    heartbeat.LastHeartbeatAt = DateTimeOffset.UtcNow;
                    heartbeat.Status = "Online";
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker heartbeat yazılamadı.");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
