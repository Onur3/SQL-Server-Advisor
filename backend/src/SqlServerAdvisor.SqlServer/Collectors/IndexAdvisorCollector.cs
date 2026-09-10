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

    private const string ServerPermissionSql = """
        SELECT
            CAST(HAS_PERMS_BY_NAME(NULL, 'SERVER', 'VIEW SERVER STATE') AS int) AS HasViewServerState,
            CAST(HAS_PERMS_BY_NAME(NULL, 'SERVER', 'VIEW ANY DATABASE') AS int) AS HasViewAnyDatabase;
        """;

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
        ),
        ops AS
        (
            SELECT
                object_id,
                index_id,
                SUM(leaf_insert_count) AS LeafInsertCount,
                SUM(leaf_delete_count) AS LeafDeleteCount,
                SUM(leaf_update_count) AS LeafUpdateCount,
                SUM(leaf_allocation_count) AS LeafAllocationCount,
                SUM(range_scan_count) AS RangeScanCount,
                SUM(singleton_lookup_count) AS SingletonLookupCount,
                SUM(page_latch_wait_in_ms) AS PageLatchWaitMs
            FROM sys.dm_db_index_operational_stats(DB_ID(), NULL, NULL, NULL)
            WHERE index_id > 0
            GROUP BY object_id, index_id
        ),
        compression AS
        (
            SELECT
                object_id,
                index_id,
                CASE
                    WHEN MIN(data_compression) = MAX(data_compression) THEN MAX(data_compression_desc)
                    ELSE N'MIXED'
                END AS DataCompression
            FROM sys.partitions
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
                  AND ic.key_ordinal > 0
                ORDER BY ic.key_ordinal
                FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N''), N'') AS KeyColumns,
            ISNULL(STUFF((
                SELECT N', ' + QUOTENAME(c.name) +
                    CASE WHEN ic.is_descending_key = 1 THEN N' DESC' ELSE N' ASC' END
                FROM sys.index_columns ic
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE ic.object_id = i.object_id
                  AND ic.index_id = i.index_id
                  AND ic.key_ordinal > 0
                ORDER BY ic.key_ordinal
                FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N''), N'') AS KeyDefinition,
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
            i.fill_factor AS FillFactor,
            cmp.DataCompression,
            DATEDIFF(day, osi.sqlserver_start_time, SYSDATETIME()) AS UsageSinceDays,
            ISNULL(op.LeafInsertCount, 0) AS LeafInsertCount,
            ISNULL(op.LeafDeleteCount, 0) AS LeafDeleteCount,
            ISNULL(op.LeafUpdateCount, 0) AS LeafUpdateCount,
            ISNULL(op.LeafAllocationCount, 0) AS LeafAllocationCount,
            ISNULL(op.RangeScanCount, 0) AS RangeScanCount,
            ISNULL(op.SingletonLookupCount, 0) AS SingletonLookupCount,
            ISNULL(op.PageLatchWaitMs, 0) AS PageLatchWaitMs,
            ips.AvgFragmentationPercent,
            ips.PageCount
        FROM sys.indexes i
        JOIN sys.tables t ON t.object_id = i.object_id
        JOIN ips ON ips.object_id = i.object_id AND ips.index_id = i.index_id
        CROSS JOIN sys.dm_os_sys_info AS osi
        LEFT JOIN ops op ON op.object_id = i.object_id AND op.index_id = i.index_id
        LEFT JOIN compression cmp ON cmp.object_id = i.object_id AND cmp.index_id = i.index_id
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

        var indexes = new List<IndexSnapshot>();
        var missingIndexes = new List<MissingIndexSnapshot>();
        var warnings = new List<string>();
        var capturedAt = DateTimeOffset.UtcNow;

        var serverPermissions = await connection.QuerySingleAsync<ServerPermissionState>(new CommandDefinition(
            ServerPermissionSql,
            commandTimeout: 10,
            cancellationToken: cancellationToken));

        if (serverPermissions.HasViewServerState != 1)
        {
            warnings.Add("Sunucu: Index Advisor çalıştırılamadı; SQL Server 2019 için VIEW SERVER STATE yetkisi eksik.");
            return new CollectorBatch
            {
                Indexes = indexes,
                MissingIndexes = missingIndexes,
                Warnings = warnings
            };
        }

        if (serverPermissions.HasViewAnyDatabase != 1)
            warnings.Add("Sunucu: VIEW ANY DATABASE yetkisi eksik; database coverage eksik olabilir.");

        var databaseNames = (await connection.QueryAsync<string>(new CommandDefinition(
            DatabaseSql,
            commandTimeout: 10,
            cancellationToken: cancellationToken))).AsList();

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
                    warnings.Add($"{databaseName}: indeks analizi atlandı; VIEW DATABASE STATE ve VIEW DEFINITION yetkileri gerekli.");
                    continue;
                }

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

                var databaseMissingIndexes = (await connection.QueryAsync<MissingIndexSnapshot>(new CommandDefinition(
                    MissingIndexSql,
                    commandTimeout: 20,
                    cancellationToken: cancellationToken))).AsList();

                var validMissingIndexes = new List<MissingIndexSnapshot>();
                foreach (var missing in databaseMissingIndexes)
                {
                    if (string.IsNullOrWhiteSpace(missing.TableName))
                    {
                        warnings.Add($"{databaseName}: missing-index kaydı tablo metadata'sı çözülemediği için atlandı; VIEW DEFINITION yetkisini kontrol edin.");
                        continue;
                    }

                    missing.ServerProfileId = server.Id;
                    missing.CapturedAt = capturedAt;
                    validMissingIndexes.Add(missing);
                }

                missingIndexes.AddRange(ConsolidateMissingIndexes(validMissingIndexes));
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

    private static IReadOnlyCollection<MissingIndexSnapshot> ConsolidateMissingIndexes(
        IReadOnlyCollection<MissingIndexSnapshot> candidates)
    {
        var result = new List<MissingIndexSnapshot>();

        var groups = candidates.GroupBy(x => string.Join('\u001e',
            x.DatabaseName.ToUpperInvariant(),
            x.TableName.ToUpperInvariant(),
            NormalizeColumnSequence(x.EqualityColumns),
            NormalizeColumnSequence(x.InequalityColumns)));

        foreach (var group in groups)
        {
            var remaining = group
                .Select(x => new MissingIndexCandidateState(
                    x,
                    ParseColumns(x.IncludedColumns).ToHashSet(StringComparer.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.IncludeColumns.Count)
                .ThenByDescending(x => x.Snapshot.ImprovementMeasure)
                .ThenByDescending(x => x.Snapshot.UserSeeks + x.Snapshot.UserScans)
                .ToList();

            while (remaining.Count > 0)
            {
                var leader = remaining[0];
                var covered = remaining
                    .Where(x => leader.IncludeColumns.IsSupersetOf(x.IncludeColumns))
                    .ToList();

                if (covered.Count > 1)
                {
                    leader.Snapshot.UserSeeks = covered.Max(x => x.Snapshot.UserSeeks);
                    leader.Snapshot.UserScans = covered.Max(x => x.Snapshot.UserScans);
                    leader.Snapshot.AvgTotalUserCost = covered.Max(x => x.Snapshot.AvgTotalUserCost);
                    leader.Snapshot.AvgUserImpact = covered.Max(x => x.Snapshot.AvgUserImpact);
                    leader.Snapshot.ImprovementMeasure = covered.Max(x => x.Snapshot.ImprovementMeasure);
                    leader.Snapshot.CapturedAt = covered.Max(x => x.Snapshot.CapturedAt);
                }

                result.Add(leader.Snapshot);
                remaining.RemoveAll(x => covered.Contains(x));
            }
        }

        return result
            .OrderByDescending(x => x.ImprovementMeasure)
            .ToList();
    }

    private static string NormalizeColumnSequence(string? value) =>
        string.Join('\u001f', ParseColumns(value).Select(x => x.ToUpperInvariant()));

    private static string[] ParseColumns(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim().Trim('[', ']', '"'))
                .Where(x => x.Length > 0)
                .ToArray();

    private sealed record MissingIndexCandidateState(
        MissingIndexSnapshot Snapshot,
        HashSet<string> IncludeColumns);

    private sealed record ServerPermissionState(int HasViewServerState, int HasViewAnyDatabase);
    private sealed record DatabasePermissionState(int HasViewDatabaseState, int HasViewDefinition);
}
