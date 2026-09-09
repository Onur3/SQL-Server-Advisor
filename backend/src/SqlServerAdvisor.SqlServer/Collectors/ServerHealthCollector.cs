using Dapper;
using Microsoft.Data.SqlClient;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.SqlServer.Collectors;

public sealed class ServerHealthCollector(IMonitoredConnectionStringFactory connectionStringFactory) : IServerHealthCollector
{
    private const string ServerSql = """
        SELECT
            CAST(SERVERPROPERTY('ServerName') AS nvarchar(255)) AS ServerName,
            CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(100)) AS ProductVersion,
            CAST(SERVERPROPERTY('Edition') AS nvarchar(255)) AS Edition,
            TODATETIMEOFFSET(sqlserver_start_time, DATEPART(TZOFFSET, SYSDATETIMEOFFSET())) AS SqlServerStartTime
        FROM sys.dm_os_sys_info;
        """;

    private const string SessionSql = """
        SELECT
            SUM(CASE WHEN is_user_process = 1 THEN 1 ELSE 0 END) AS UserConnections,
            SUM(CASE WHEN is_user_process = 1 AND status IN ('running','runnable','suspended') THEN 1 ELSE 0 END) AS ActiveSessions
        FROM sys.dm_exec_sessions
        WHERE program_name IS NULL OR program_name <> 'SQLServerAdvisor.Worker';

        SELECT
            COUNT(*) AS ActiveRequests,
            ISNULL(SUM(CASE WHEN blocking_session_id > 0 THEN 1 ELSE 0 END), 0) AS BlockedRequests
        FROM sys.dm_exec_requests
        WHERE session_id <> @@SPID;
        """;

    private const string MemorySql = """
        SELECT
            total_physical_memory_kb / 1024 AS PhysicalMemoryMb,
            available_physical_memory_kb / 1024 AS AvailableMemoryMb
        FROM sys.dm_os_sys_memory;

        SELECT physical_memory_in_use_kb / 1024 AS SqlMemoryMb
        FROM sys.dm_os_process_memory;

        SELECT MIN(cntr_value) AS PageLifeExpectancy
        FROM sys.dm_os_performance_counters
        WHERE counter_name = 'Page life expectancy'
          AND object_name LIKE '%Buffer Manager%';
        """;

    private const string CpuSql = """
        SELECT TOP (1)
            record.value('(./Record/SchedulerMonitorEvent/SystemHealth/ProcessUtilization)[1]', 'int') AS SqlCpuPercent,
            record.value('(./Record/SchedulerMonitorEvent/SystemHealth/SystemIdle)[1]', 'int') AS SystemIdlePercent
        FROM (
            SELECT timestamp,
                   CONVERT(xml, record) AS record
            FROM sys.dm_os_ring_buffers
            WHERE ring_buffer_type = N'RING_BUFFER_SCHEDULER_MONITOR'
              AND record LIKE '%<SystemHealth>%'
        ) AS x
        ORDER BY timestamp DESC;
        """;

    public async Task<ServerSnapshot> CollectAsync(ServerProfile server, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionStringFactory.Create(server));
        await connection.OpenAsync(cancellationToken);

        var serverInfo = await connection.QuerySingleAsync<ServerInfo>(new CommandDefinition(ServerSql, commandTimeout: 5, cancellationToken: cancellationToken));

        int activeSessions = 0, userConnections = 0, activeRequests = 0, blockedRequests = 0;
        long? physicalMemoryMb = null, availableMemoryMb = null, sqlMemoryMb = null, ple = null;
        int? sqlCpu = null, systemIdle = null;
        var sessionMetricsAvailable = false;
        var memoryMetricsAvailable = false;
        var cpuMetricsAvailable = false;

        try
        {
            using var sessions = await connection.QueryMultipleAsync(new CommandDefinition(SessionSql, commandTimeout: 5, cancellationToken: cancellationToken));
            var sessionRow = await sessions.ReadSingleAsync<SessionInfo>();
            var requestRow = await sessions.ReadSingleAsync<RequestInfo>();
            activeSessions = sessionRow.ActiveSessions;
            userConnections = sessionRow.UserConnections;
            activeRequests = requestRow.ActiveRequests;
            blockedRequests = requestRow.BlockedRequests;
            sessionMetricsAvailable = true;
        }
        catch (SqlException ex) when (ex.Number == 297 || ex.Number == 229) { }

        try
        {
            using var memory = await connection.QueryMultipleAsync(new CommandDefinition(MemorySql, commandTimeout: 5, cancellationToken: cancellationToken));
            var os = await memory.ReadSingleAsync<OsMemory>();
            var process = await memory.ReadSingleAsync<ProcessMemory>();
            var counter = await memory.ReadFirstOrDefaultAsync<PleCounter>();
            physicalMemoryMb = os.PhysicalMemoryMb;
            availableMemoryMb = os.AvailableMemoryMb;
            sqlMemoryMb = process.SqlMemoryMb;
            ple = counter?.PageLifeExpectancy;
            memoryMetricsAvailable = true;
        }
        catch (SqlException ex) when (ex.Number == 297 || ex.Number == 229) { }

        try
        {
            var cpu = await connection.QueryFirstOrDefaultAsync<CpuInfo>(new CommandDefinition(CpuSql, commandTimeout: 5, cancellationToken: cancellationToken));
            sqlCpu = cpu?.SqlCpuPercent;
            systemIdle = cpu?.SystemIdlePercent;
            cpuMetricsAvailable = cpu is not null;
        }
        catch (SqlException ex) when (ex.Number == 297 || ex.Number == 229) { }

        return new ServerSnapshot
        {
            ServerProfileId = server.Id,
            CapturedAt = DateTimeOffset.UtcNow,
            ServerName = serverInfo.ServerName,
            ProductVersion = serverInfo.ProductVersion,
            Edition = serverInfo.Edition,
            SqlServerStartTime = serverInfo.SqlServerStartTime,
            SqlCpuPercent = sqlCpu,
            SystemIdlePercent = systemIdle,
            PhysicalMemoryMb = physicalMemoryMb,
            AvailableMemoryMb = availableMemoryMb,
            SqlMemoryMb = sqlMemoryMb,
            PageLifeExpectancy = ple,
            ActiveSessions = activeSessions,
            ActiveRequests = activeRequests,
            BlockedRequests = blockedRequests,
            UserConnections = userConnections,
            SessionMetricsAvailable = sessionMetricsAvailable,
            MemoryMetricsAvailable = memoryMetricsAvailable,
            CpuMetricsAvailable = cpuMetricsAvailable
        };
    }

    private sealed record ServerInfo(string? ServerName, string? ProductVersion, string? Edition, DateTimeOffset? SqlServerStartTime);
    private sealed record SessionInfo(int UserConnections, int ActiveSessions);
    private sealed record RequestInfo(int ActiveRequests, int BlockedRequests);
    private sealed record OsMemory(long PhysicalMemoryMb, long AvailableMemoryMb);
    private sealed record ProcessMemory(long SqlMemoryMb);
    private sealed record PleCounter(long PageLifeExpectancy);
    private sealed record CpuInfo(int SqlCpuPercent, int SystemIdlePercent);
}
