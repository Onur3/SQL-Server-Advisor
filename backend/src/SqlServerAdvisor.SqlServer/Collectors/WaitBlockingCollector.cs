using Dapper;
using Microsoft.Data.SqlClient;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.SqlServer.Collectors;

public sealed class WaitBlockingCollector(IMonitoredConnectionStringFactory connectionStringFactory) : IAdvisorCollector
{
    public string Name => "WaitBlocking";
    public int DefaultIntervalSeconds => 30;
    public int DefaultTimeoutSeconds => 10;

    private const string WaitSql = """
        SELECT
            wait_type AS WaitType,
            waiting_tasks_count AS WaitingTasks,
            wait_time_ms AS WaitTimeMs,
            signal_wait_time_ms AS SignalWaitTimeMs
        FROM sys.dm_os_wait_stats
        WHERE wait_time_ms > 0
          AND wait_type NOT LIKE N'SLEEP%'
          AND wait_type NOT IN
          (
              N'BROKER_EVENTHANDLER', N'BROKER_RECEIVE_WAITFOR', N'BROKER_TASK_STOP',
              N'BROKER_TO_FLUSH', N'CHECKPOINT_QUEUE', N'CHKPT', N'CLR_AUTO_EVENT',
              N'CLR_MANUAL_EVENT', N'DIRTY_PAGE_POLL', N'DISPATCHER_QUEUE_SEMAPHORE',
              N'FT_IFTS_SCHEDULER_IDLE_WAIT', N'HADR_FILESTREAM_IOMGR_IOCOMPLETION',
              N'LAZYWRITER_SLEEP', N'LOGMGR_QUEUE', N'ONDEMAND_TASK_QUEUE',
              N'REQUEST_FOR_DEADLOCK_SEARCH', N'RESOURCE_QUEUE', N'SERVER_IDLE_CHECK',
              N'SLEEP_BPOOL_FLUSH', N'SNI_HTTP_ACCEPT', N'SP_SERVER_DIAGNOSTICS_SLEEP',
              N'SQLTRACE_BUFFER_FLUSH', N'SQLTRACE_INCREMENTAL_FLUSH_SLEEP', N'WAITFOR',
              N'WAITFOR_TASKSHUTDOWN', N'XE_DISPATCHER_JOIN', N'XE_DISPATCHER_WAIT',
              N'XE_TIMER_EVENT'
          )
        ORDER BY wait_time_ms DESC;
        """;

    private const string BlockingSql = """
        SELECT
            CONVERT(int, r.session_id) AS SessionId,
            CONVERT(int, r.blocking_session_id) AS BlockingSessionId,
            DB_NAME(r.database_id) AS DatabaseName,
            r.wait_type AS WaitType,
            CONVERT(bigint, r.wait_time) AS WaitTimeMs,
            r.wait_resource AS WaitResource,
            txt.text AS SqlText,
            s.host_name AS HostName,
            s.program_name AS ProgramName,
            s.login_name AS LoginName
        FROM sys.dm_exec_requests AS r
        INNER JOIN sys.dm_exec_sessions AS s
            ON s.session_id = r.session_id
        OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) AS txt
        WHERE r.session_id <> @@SPID
          AND r.blocking_session_id > 0
          AND (s.program_name IS NULL OR s.program_name <> N'SQLServerAdvisor.Worker')
        ORDER BY r.wait_time DESC;
        """;

    public async Task<CollectorBatch> CollectAsync(ServerProfile server, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionStringFactory.Create(server));
        await connection.OpenAsync(cancellationToken);

        var waits = (await connection.QueryAsync<WaitObservation>(new CommandDefinition(
            WaitSql,
            commandTimeout: DefaultTimeoutSeconds,
            cancellationToken: cancellationToken))).AsList();

        var capturedAt = DateTimeOffset.UtcNow;
        var blockingRows = (await connection.QueryAsync<BlockingRow>(new CommandDefinition(
            BlockingSql,
            commandTimeout: DefaultTimeoutSeconds,
            cancellationToken: cancellationToken))).AsList();

        var blocking = blockingRows.Select(x => new BlockingEvent
        {
            ServerProfileId = server.Id,
            SessionId = x.SessionId,
            BlockingSessionId = x.BlockingSessionId,
            DatabaseName = x.DatabaseName,
            WaitType = x.WaitType,
            WaitTimeMs = x.WaitTimeMs,
            WaitResource = x.WaitResource,
            SqlText = x.SqlText,
            HostName = x.HostName,
            ProgramName = x.ProgramName,
            LoginName = x.LoginName,
            CapturedAt = capturedAt
        }).ToArray();

        return new CollectorBatch
        {
            Waits = waits,
            Blocking = blocking
        };
    }

    private sealed record BlockingRow(
        int SessionId,
        int BlockingSessionId,
        string? DatabaseName,
        string? WaitType,
        long WaitTimeMs,
        string? WaitResource,
        string? SqlText,
        string? HostName,
        string? ProgramName,
        string? LoginName);
}
