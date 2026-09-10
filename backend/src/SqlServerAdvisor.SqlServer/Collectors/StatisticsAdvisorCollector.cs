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

    private const string DatabasePermissionSql = """
        SELECT
            CAST(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DATABASE STATE') AS int) AS HasViewDatabaseState,
            CAST(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION') AS int) AS HasViewDefinition;
        """;

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
            ISNULL(STUFF((
                SELECT N', ' + QUOTENAME(c.name)
                FROM sys.stats_columns sc
                JOIN sys.columns c ON c.object_id = sc.object_id AND c.column_id = sc.column_id
                WHERE sc.object_id = s.object_id
                  AND sc.stats_id = s.stats_id
                ORDER BY sc.stats_column_id
                FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N''), N'') AS StatisticsColumns,
            ISNULL(sp.rows, 0) AS [Rows],
            ISNULL(sp.rows_sampled, 0) AS RowsSampled,
            ISNULL(sp.modification_counter, 0) AS ModificationCounter,
            sp.last_updated AS LastUpdated,
            CAST(CASE WHEN ISNULL(sp.rows, 0) = 0 THEN NULL
                      ELSE sp.rows_sampled * 100.0 / NULLIF(sp.rows, 0)
                 END AS decimal(9,3)) AS SamplePercent,
            sp.steps AS Steps,
            sp.unfiltered_rows AS UnfilteredRows,
            CAST(sp.persisted_sample_percent AS decimal(9,3)) AS PersistedSamplePercent,
            s.auto_created AS AutoCreated,
            s.user_created AS UserCreated,
            s.no_recompute AS NoRecompute,
            s.has_filter AS HasFilter,
            s.filter_definition AS FilterDefinition,
            s.is_incremental AS IsIncremental,
            CAST(CASE WHEN sp.object_id IS NULL THEN 0 ELSE 1 END AS bit) AS PropertiesVisible
        FROM sys.stats s
        JOIN sys.tables t ON t.object_id = s.object_id
        OUTER APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) sp
        WHERE t.is_ms_shipped = 0
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
        var warnings = new List<string>();
        var capturedAt = DateTimeOffset.UtcNow;

        foreach (var databaseName in databaseNames)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                connection.ChangeDatabase(databaseName);

                var databasePermissions = await connection.QuerySingleAsync<DatabasePermissionState>(new CommandDefinition(
                    DatabasePermissionSql,
                    commandTimeout: 10,
                    cancellationToken: cancellationToken));

                if (databasePermissions.HasViewDatabaseState != 1 || databasePermissions.HasViewDefinition != 1)
                {
                    warnings.Add($"{databaseName}: statistics analizi atlandı; VIEW DATABASE STATE ve VIEW DEFINITION yetkileri gerekli.");
                    continue;
                }

                var rows = (await connection.QueryAsync<StatisticsSnapshot>(new CommandDefinition(
                    StatisticsSql,
                    commandTimeout: DefaultTimeoutSeconds,
                    cancellationToken: cancellationToken))).AsList();

                if (rows.Count > 0)
                {
                    var visibleCount = rows.Count(x => x.PropertiesVisible);
                    if (visibleCount == 0)
                    {
                        warnings.Add($"{databaseName}: sys.dm_db_stats_properties hiçbir statistics için görünür değil; statistics tuning coverage eksik. Worker'a kullanıcı verisi SELECT yetkisi vermek yerine güvenli metadata gateway/module signing kullanın.");
                        continue;
                    }

                    if (visibleCount < rows.Count)
                        warnings.Add($"{databaseName}: statistics metadata kısmi; {visibleCount:N0}/{rows.Count:N0} statistics için dm_db_stats_properties görünür. Eksik nesneler analiz edilmedi.");
                }

                foreach (var item in rows.Where(x => x.PropertiesVisible && x.Rows >= 1000))
                {
                    item.ServerProfileId = server.Id;
                    item.CapturedAt = capturedAt;
                    statistics.Add(item);
                }
            }
            catch (SqlException ex) when (ex.Number is 229 or 297 or 916)
            {
                warnings.Add($"{databaseName}: statistics analizi atlandı; CONNECT / VIEW DATABASE STATE / VIEW DEFINITION ve statistics metadata görünürlüğünü kontrol edin. SQL {ex.Number}.");
            }
        }

        return new CollectorBatch
        {
            Statistics = statistics,
            Warnings = warnings
        };
    }

    private sealed record DatabasePermissionState(int HasViewDatabaseState, int HasViewDefinition);
}
