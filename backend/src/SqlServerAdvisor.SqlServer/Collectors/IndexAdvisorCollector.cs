using Dapper;
using Microsoft.Data.SqlClient;
using SqlServerAdvisor.Application.Contracts;
using SqlServerAdvisor.Application.DTOs;
using SqlServerAdvisor.Domain.Entities;

namespace SqlServerAdvisor.SqlServer.Collectors;

public sealed class IndexAdvisorCollector(IMonitoredConnectionStringFactory connectionStringFactory) : IAdvisorCollector
{
    public string Name => "IndexAdvisor";
    public int DefaultIntervalSeconds => 900;
    public int DefaultTimeoutSeconds => 60;

    private const string DatabaseSql = """
        SELECT name
        FROM sys.databases
        WHERE database_id > 4
          AND state = 0
          AND source_database_id IS NULL
        ORDER BY name;
        """;

    private const string IndexSql = """
        WITH ips AS
        (
            SELECT
                object_id,
                index_id,
                SUM(page_count) AS PageCount,
                CAST(SUM(CAST(avg_fragmentation_in_percent AS decimal(19,6)) * page_count) /
                     NULLIF(SUM(page_count), 0) AS decimal(9,3)) AS AvgFragmentationPercent
            FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED')
            WHERE index_id > 0
            GROUP BY object_id, index_id
        )
        SELECT
            DB_NAME() AS DatabaseName,
            i.object_id AS ObjectId,
            i.index_id AS IndexId,
            QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) AS TableName,
            ISNULL(i.name, N'<unnamed>') AS IndexName,
            i.type_desc AS TypeDesc,
            ISNULL(STUFF((
                SELECT N', ' + QUOTENAME(c.name)
                FROM sys.index_columns ic
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE ic.object_id = i.object_id
                  AND ic.index_id = i.index_id
                  AND ic.is_included_column = 0
                ORDER BY ic.key_ordinal
                FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N''), N'') AS KeyColumns,
            ISNULL(STUFF((
                SELECT N', ' + QUOTENAME(c.name)
                FROM sys.index_columns ic
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE ic.object_id = i.object_id
                  AND ic.index_id = i.index_id
                  AND ic.is_included_column = 1
                ORDER BY ic.index_column_id
                FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N''), N'') AS IncludeColumns,
            CAST(ips.PageCount * 8.0 / 1024.0 AS decimal(19,2)) AS SizeMb,
            ISNULL(us.user_seeks, 0) AS UserSeeks,
            ISNULL(us.user_scans, 0) AS UserScans,
            ISNULL(us.user_lookups, 0) AS UserLookups,
            ISNULL(us.user_updates, 0) AS UserUpdates,
            i.is_unique AS IsUnique,
            i.is_primary_key AS IsPrimaryKey,
            i.is_disabled AS IsDisabled,
            i.has_filter AS HasFilter,
            i.filter_definition AS FilterDefinition,
            DATEDIFF(day, osi.sqlserver_start_time, SYSDATETIME()) AS UsageSinceDays,
            ips.AvgFragmentationPercent,
            ips.PageCount
        FROM sys.indexes i
        JOIN sys.tables t ON t.object_id = i.object_id
        JOIN ips ON ips.object_id = i.object_id AND ips.index_id = i.index_id
        CROSS JOIN sys.dm_os_sys_info AS osi
        LEFT JOIN sys.dm_db_index_usage_stats us
          ON us.database_id = DB_ID()
         AND us.object_id = i.object_id
         AND us.index_id = i.index_id
        WHERE i.index_id > 0
          AND i.is_hypothetical = 0
        ORDER BY ips.PageCount DESC;
        """;

    private const string MissingIndexSql = """
        SELECT TOP (50)
            DB_NAME() AS DatabaseName,
            QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) AS TableName,
            ISNULL(mid.equality_columns, N'') AS EqualityColumns,
            ISNULL(mid.inequality_columns, N'') AS InequalityColumns,
            ISNULL(mid.included_columns, N'') AS IncludedColumns,
            ISNULL(migs.user_seeks, 0) AS UserSeeks,
            ISNULL(migs.user_scans, 0) AS UserScans,
            CAST(ISNULL(migs.avg_total_user_cost, 0) AS decimal(19,3)) AS AvgTotalUserCost,
            CAST(ISNULL(migs.avg_user_impact, 0) AS decimal(9,3)) AS AvgUserImpact,
            CAST(ISNULL(migs.avg_total_user_cost, 0) * ISNULL(migs.avg_user_impact, 0) *
                 (ISNULL(migs.user_seeks, 0) + ISNULL(migs.user_scans, 0)) AS decimal(19,3)) AS ImprovementMeasure
        FROM sys.dm_db_missing_index_details mid
        JOIN sys.dm_db_missing_index_groups mig ON mig.index_handle = mid.index_handle
        JOIN sys.dm_db_missing_index_group_stats migs ON migs.group_handle = mig.index_group_handle
        JOIN sys.tables t ON t.object_id = mid.object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE mid.database_id = DB_ID()
          AND (ISNULL(migs.user_seeks, 0) + ISNULL(migs.user_scans, 0)) >= 10
        ORDER BY ImprovementMeasure DESC;
        """;

    public async Task<CollectorBatch> CollectAsync(ServerProfile server, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionStringFactory.Create(server));
        await connection.OpenAsync(cancellationToken);

        var databaseNames = (await connection.QueryAsync<string>(new CommandDefinition(
            DatabaseSql,
            commandTimeout: 10,
            cancellationToken: cancellationToken))).AsList();

        var indexes = new List<IndexSnapshot>();
        var missingIndexes = new List<MissingIndexSnapshot>();
        var warnings = new List<string>();
        var capturedAt = DateTimeOffset.UtcNow;

        foreach (var databaseName in databaseNames)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                connection.ChangeDatabase(databaseName);

                // Collect the complete index catalog, including small indexes. Fragmentation rules
                // still apply their own page-count threshold, but missing-index coverage must compare
                // against every existing usable index or it can recommend duplicates.
                var databaseIndexes = (await connection.QueryAsync<IndexSnapshot>(new CommandDefinition(
                    IndexSql,
                    commandTimeout: DefaultTimeoutSeconds,
                    cancellationToken: cancellationToken))).AsList();

                foreach (var index in databaseIndexes)
                {
                    index.ServerProfileId = server.Id;
                    index.CapturedAt = capturedAt;
                    indexes.Add(index);
                }

                // Resolve schema/table from the current database catalog rather than OBJECT_NAME().
                // Without metadata visibility OBJECT_NAME/OBJECT_SCHEMA_NAME can return NULL silently,
                // which previously produced malformed recommendations such as "ON  ([Column])".
                var databaseMissingIndexes = (await connection.QueryAsync<MissingIndexSnapshot>(new CommandDefinition(
                    MissingIndexSql,
                    commandTimeout: 20,
                    cancellationToken: cancellationToken))).AsList();

                foreach (var missing in databaseMissingIndexes)
                {
                    if (string.IsNullOrWhiteSpace(missing.TableName))
                    {
                        warnings.Add($"{databaseName}: missing-index kaydı tablo metadata'sı çözülemediği için atlandı; VIEW DEFINITION yetkisini kontrol edin.");
                        continue;
                    }

                    missing.ServerProfileId = server.Id;
                    missing.CapturedAt = capturedAt;
                    missingIndexes.Add(missing);
                }
            }
            catch (SqlException ex) when (ex.Number is 229 or 297 or 916)
            {
                warnings.Add($"{databaseName}: indeks analizi atlandı; SQL Server 2019 için VIEW SERVER STATE ve bu veritabanında CONNECT / VIEW DATABASE STATE / VIEW DEFINITION yetkilerini kontrol edin. SQL {ex.Number}.");
            }
        }

        return new CollectorBatch
        {
            Indexes = indexes,
            MissingIndexes = missingIndexes,
            Warnings = warnings
        };
    }
}
