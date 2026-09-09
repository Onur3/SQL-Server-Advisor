using Dapper;
using Microsoft.Data.SqlClient;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.SqlServer.Collectors;

public sealed class StatisticsAdvisorCollector(IMonitoredConnectionStringFactory connectionStringFactory) : IAdvisorCollector
{
    public string Name => "StatisticsAdvisor";
    public int DefaultIntervalSeconds => 3600;
    public int DefaultTimeoutSeconds => 30;

    private const string DatabaseSql = """
        SELECT name
        FROM sys.databases
        WHERE database_id > 4
          AND state = 0
          AND source_database_id IS NULL
        ORDER BY name;
        """;

    private const string StatisticsSql = """
        SELECT
            DB_NAME() AS DatabaseName,
            s.object_id AS ObjectId,
            s.stats_id AS StatisticsId,
            QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) AS TableName,
            s.name AS StatisticsName,
            ISNULL(sp.rows, 0) AS [Rows],
            ISNULL(sp.rows_sampled, 0) AS RowsSampled,
            ISNULL(sp.modification_counter, 0) AS ModificationCounter,
            sp.last_updated AS LastUpdated,
            CAST(CASE WHEN ISNULL(sp.rows, 0) = 0 THEN NULL
                      ELSE sp.rows_sampled * 100.0 / NULLIF(sp.rows, 0)
                 END AS decimal(9,3)) AS SamplePercent,
            s.auto_created AS AutoCreated,
            s.user_created AS UserCreated,
            s.no_recompute AS NoRecompute,
            s.has_filter AS HasFilter,
            s.filter_definition AS FilterDefinition
        FROM sys.stats s
        JOIN sys.tables t ON t.object_id = s.object_id
        OUTER APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) sp
        WHERE t.is_ms_shipped = 0
          AND ISNULL(sp.rows, 0) >= 1000
        ORDER BY ISNULL(sp.modification_counter, 0) DESC;
        """;

    public async Task<CollectorBatch> CollectAsync(ServerProfile server, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionStringFactory.Create(server));
        await connection.OpenAsync(cancellationToken);

        var databaseNames = (await connection.QueryAsync<string>(new CommandDefinition(
            DatabaseSql,
            commandTimeout: 10,
            cancellationToken: cancellationToken))).AsList();

        var statistics = new List<StatisticsSnapshot>();
        var capturedAt = DateTimeOffset.UtcNow;

        foreach (var databaseName in databaseNames)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                connection.ChangeDatabase(databaseName);
                var rows = (await connection.QueryAsync<StatisticsSnapshot>(new CommandDefinition(
                    StatisticsSql,
                    commandTimeout: DefaultTimeoutSeconds,
                    cancellationToken: cancellationToken))).AsList();

                foreach (var item in rows)
                {
                    item.ServerProfileId = server.Id;
                    item.CapturedAt = capturedAt;
                    statistics.Add(item);
                }
            }
            catch (SqlException ex) when (ex.Number is 229 or 297 or 916)
            {
                // Skip databases where the monitoring identity lacks read-only metadata access.
            }
        }

        return new CollectorBatch { Statistics = statistics };
    }
}
