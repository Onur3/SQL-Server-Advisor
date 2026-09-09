using Dapper;
using Microsoft.Data.SqlClient;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Domain.Entities;
namespace SqlServerAdvisor.SqlServer.Collectors;

public sealed class WaitBlockingCollector(IMonitoredConnectionStringFactory factory)
{
    public async Task<List<WaitSnapshot>> WaitsAsync(ServerProfile server, int timeout, CancellationToken ct)
    {
        await using var connection = new SqlConnection(factory.Create(server));
        await connection.OpenAsync(ct);
        await RequireVisibilityAsync(connection, timeout, ct);
        var rows = (await connection.QueryAsync<WaitSnapshot>(new CommandDefinition("""
            SELECT wait_type AS WaitType, waiting_tasks_count AS WaitingTasks,
                wait_time_ms AS WaitTimeMs, signal_wait_time_ms AS SignalWaitTimeMs,
                i.sqlserver_start_time AS SqlServerStartTime
            FROM sys.dm_os_wait_stats CROSS JOIN sys.dm_os_sys_info i;
            """, commandTimeout: timeout, cancellationToken: ct))).ToList();
        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows) { row.ServerProfileId = server.Id; row.CapturedAt = now; }
        return rows;
    }

    public async Task<List<BlockingEvent>> BlockingAsync(ServerProfile server, int timeout, CancellationToken ct)
    {
        await using var connection = new SqlConnection(factory.Create(server));
        await connection.OpenAsync(ct);
        await RequireVisibilityAsync(connection, timeout, ct);
        var rows = (await connection.QueryAsync<BlockingEvent>(new CommandDefinition("""
            SELECT r.session_id AS SessionId, r.request_id AS RequestId,
                r.blocking_session_id AS BlockingSessionId, DB_NAME(r.database_id) AS DatabaseName,
                r.wait_type AS WaitType, CONVERT(bigint,r.wait_time) AS WaitTimeMs,
                r.wait_resource AS WaitResource, LEFT(t.text,4000) AS SqlText,
                s.host_name AS HostName, s.program_name AS ProgramName, s.login_name AS LoginName,
                bs.status AS BlockerStatus, bs.open_transaction_count AS BlockerOpenTransactions,
                LEFT(bt.text,4000) AS BlockerSqlText
            FROM sys.dm_exec_requests r
            JOIN sys.dm_exec_sessions s ON s.session_id=r.session_id
            LEFT JOIN sys.dm_exec_sessions bs ON bs.session_id=r.blocking_session_id
            OUTER APPLY (SELECT TOP (1) c.most_recent_sql_handle FROM sys.dm_exec_connections c
                         WHERE c.session_id=r.blocking_session_id ORDER BY c.connection_id) bc
            OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) t
            OUTER APPLY sys.dm_exec_sql_text(bc.most_recent_sql_handle) bt
            WHERE r.blocking_session_id <> 0 AND r.session_id <> @@SPID AND s.is_user_process=1;
            """, commandTimeout: timeout, cancellationToken: ct))).ToList();
        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows) { row.ServerProfileId = server.Id; row.CapturedAt = now; }
        return rows;
    }
    private static async Task RequireVisibilityAsync(SqlConnection connection, int timeout, CancellationToken ct)
    {
        var allowed = await connection.QuerySingleAsync<int?>(new CommandDefinition("""
            SELECT CASE WHEN CONVERT(int, SERVERPROPERTY('ProductMajorVersion')) >= 16
                THEN HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW SERVER PERFORMANCE STATE')
                ELSE HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW SERVER STATE') END;
            """, commandTimeout: timeout, cancellationToken: ct));
        if (allowed != 1) throw new UnauthorizedAccessException("Full DMV visibility requires VIEW SERVER STATE (2019) or VIEW SERVER PERFORMANCE STATE (2022+).");
    }
}
