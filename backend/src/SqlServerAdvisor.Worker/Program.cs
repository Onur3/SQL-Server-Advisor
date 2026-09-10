using SqlServerAdvisor.Analysis;
using SqlServerAdvisor.Infrastructure;
using SqlServerAdvisor.Infrastructure.Logging;
using SqlServerAdvisor.SqlServer;
using SqlServerAdvisor.Worker.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddSqlAdvisorFileLogging(builder.Configuration, "sqladvisor-worker");

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "SQL Server Advisor Collector";
});

builder.Services.AddAdvisorInfrastructure(builder.Configuration);
builder.Services.AddMonitoredSqlServer();
builder.Services.AddAdvisorAnalysis();
builder.Services.AddHostedService<HeartbeatWorker>();
builder.Services.AddHostedService<ServerSnapshotWorker>();
builder.Services.AddHostedService<TelemetryWorker>();
builder.Services.AddHostedService<WorkloadFileWorker>();

var host = builder.Build();
await host.RunAsync();
