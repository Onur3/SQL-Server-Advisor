using Dapper;
using Microsoft.Data.SqlClient;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.SqlServer.Collectors;

public sealed class QueryPerformanceCollector(IMonitoredConnectionStringFactory connectionStringFactory) : IAdvisorCollector
{
    public string Name => "QueryPerformance";
    public int DefaultIntervalSeconds => 120;
    public int DefaultTimeoutSeconds => 15;

    private const string QuerySql = """
        SELECT TOP (50)
            COALESCE(DB_NAME(ids.ResolvedDbId), N'Ad-hoc / DB bağlamı yok') AS DatabaseName,
            CONVERT(varchar(130), qs.query_hash, 1) AS QueryHash,
            CONVERT(varchar(130), qs.query_hash, 1) AS NormalizedHash,
            ids.ResolvedObjectId AS ObjectId,
            CASE
                WHEN ids.ResolvedDbId IS NULL OR ids.ResolvedObjectId IS NULL OR ids.ResolvedObjectId <= 0 THEN NULL
                ELSE QUOTENAME(OBJECT_SCHEMA_NAME(ids.ResolvedObjectId, ids.ResolvedDbId))
                   + N'.' + QUOTENAME(OBJECT_NAME(ids.ResolvedObjectId, ids.ResolvedDbId))
            END AS ObjectName,
            SUBSTRING(
                st.text,
                (qs.statement_start_offset / 2) + 1,
                ((CASE qs.statement_end_offset
                    WHEN -1 THEN DATALENGTH(st.text)
                    ELSE qs.statement_end_offset
                  END - qs.statement_start_offset) / 2) + 1) AS StatementText,
            N'PlanCache' AS Source,
            CONVERT(varchar(130), qs.query_plan_hash, 1) AS PlanHash,
            CONVERT(nvarchar(max), qp.query_plan) AS PlanXml,
            CAST(0 AS bit) AS HasActualRuntimeCounters,
            qs.execution_count AS ExecutionCount,
            CAST(qs.total_worker_time / 1000.0 AS decimal(19,3)) AS TotalCpuMs,
            CAST((qs.total_worker_time / NULLIF(qs.execution_count, 0)) / 1000.0 AS decimal(19,3)) AS AverageCpuMs,
            CAST(qs.total_elapsed_time / 1000.0 AS decimal(19,3)) AS TotalDurationMs,
            CAST((qs.total_elapsed_time / NULLIF(qs.execution_count, 0)) / 1000.0 AS decimal(19,3)) AS AverageDurationMs,
            qs.total_logical_reads AS TotalLogicalReads,
            CAST(qs.total_logical_reads * 1.0 / NULLIF(qs.execution_count, 0) AS decimal(19,3)) AS AverageLogicalReads,
            qs.total_logical_writes AS TotalLogicalWrites,
            TODATETIMEOFFSET(qs.last_execution_time, DATEPART(TZOFFSET, SYSDATETIMEOFFSET())) AS LastExecutionTime
        FROM sys.dm_exec_query_stats AS qs
        CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS st
        OUTER APPLY sys.dm_exec_query_plan(qs.plan_handle) AS qp
        OUTER APPLY
        (
            SELECT TOP (1) TRY_CONVERT(int, pa.value) AS DbId
            FROM sys.dm_exec_plan_attributes(qs.plan_handle) AS pa
            WHERE pa.attribute = N'dbid'
        ) AS planDb
        CROSS APPLY
        (
            SELECT
                COALESCE(NULLIF(st.dbid, 0), NULLIF(qp.dbid, 0), NULLIF(planDb.DbId, 0)) AS ResolvedDbId,
                COALESCE(NULLIF(st.objectid, 0), NULLIF(qp.objectid, 0)) AS ResolvedObjectId
        ) AS ids
        WHERE st.text IS NOT NULL
          AND st.text NOT LIKE N'%sys.dm_exec_query_stats%'
          AND st.text NOT LIKE N'%SQLServerAdvisor%'
          AND qs.execution_count > 0
        ORDER BY qs.total_worker_time DESC, qs.total_logical_reads DESC;
        """;

    public async Task<CollectorBatch> CollectAsync(ServerProfile server, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionStringFactory.Create(server));
        await connection.OpenAsync(cancellationToken);

        var rows = (await connection.QueryAsync<QueryObservation>(new CommandDefinition(
            QuerySql,
            commandTimeout: DefaultTimeoutSeconds,
            cancellationToken: cancellationToken))).AsList();

        return new CollectorBatch { Queries = rows };
    }
}
