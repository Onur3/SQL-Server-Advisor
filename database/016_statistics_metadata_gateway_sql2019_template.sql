/*
 SQL Server Advisor - SQL Server 2019 statistics metadata gateway TEMPLATE
 ------------------------------------------------------------------------
 MANUAL DBA ACTION. This filename contains "template" so deploy/install.ps1 will NOT run it.

 Purpose:
 - The Advisor worker keeps NO direct SELECT permission on application tables.
 - A certificate user (WITHOUT LOGIN) owns the additional SELECT permission.
 - Only this signed stored procedure receives that certificate permission while executing.
 - The procedure returns statistics metadata only; it never returns application row data.

 Before running:
 1) Replace [CHANGE_ME_DATABASE] with one monitored database.
 2) Replace CHANGE_ME_STRONG_CERTIFICATE_PASSWORD and keep that password securely.
 3) Ensure database user [NT SERVICE\SQLServerAdvisorWorker] exists (013 template does this).
 4) Re-run this template for every monitored database that needs Statistics Advisor coverage.

 IMPORTANT: CREATE OR ALTER PROCEDURE drops an existing module signature. Therefore this template
 recreates/updates the procedure and immediately signs it again.
*/

USE [CHANGE_ME_DATABASE];
GO

IF USER_ID(N'NT SERVICE\SQLServerAdvisorWorker') IS NULL
BEGIN
    THROW 51000, 'NT SERVICE\SQLServerAdvisorWorker database user is missing. Run the monitored-worker permissions template first.', 1;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.certificates WHERE name = N'SqlAdvisorStatisticsMetadataCert')
BEGIN
    CREATE CERTIFICATE [SqlAdvisorStatisticsMetadataCert]
        ENCRYPTION BY PASSWORD = 'CHANGE_ME_STRONG_CERTIFICATE_PASSWORD'
        WITH SUBJECT = 'SQL Server Advisor statistics metadata module signing';
END
GO

IF USER_ID(N'SqlAdvisorStatisticsMetadataCertUser') IS NULL
BEGIN
    CREATE USER [SqlAdvisorStatisticsMetadataCertUser]
        FROM CERTIFICATE [SqlAdvisorStatisticsMetadataCert];
END
GO

/*
 The certificate principal has no login, so this database-level SELECT cannot be used to connect
 and read business data directly. Its permission token is added only while a signed module runs.
*/
GRANT SELECT TO [SqlAdvisorStatisticsMetadataCertUser];
GO

CREATE OR ALTER PROCEDURE dbo.usp_SQLAdvisor_StatisticsMetadata
AS
BEGIN
    SET NOCOUNT ON;

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
        CAST(1 AS bit) AS PropertiesVisible
    FROM sys.stats s
    JOIN sys.tables t ON t.object_id = s.object_id
    OUTER APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) sp
    WHERE t.is_ms_shipped = 0
    ORDER BY ISNULL(sp.modification_counter, 0) DESC;
END
GO

ADD SIGNATURE TO OBJECT::dbo.usp_SQLAdvisor_StatisticsMetadata
    BY CERTIFICATE [SqlAdvisorStatisticsMetadataCert]
    WITH PASSWORD = 'CHANGE_ME_STRONG_CERTIFICATE_PASSWORD';
GO

GRANT EXECUTE ON OBJECT::dbo.usp_SQLAdvisor_StatisticsMetadata
    TO [NT SERVICE\SQLServerAdvisorWorker];
GO

/* Effective-access verification: procedure should succeed, direct table SELECT should still fail. */
EXECUTE AS USER = N'NT SERVICE\SQLServerAdvisorWorker';
GO
EXEC dbo.usp_SQLAdvisor_StatisticsMetadata;
GO
REVERT;
GO

PRINT N'SQL Server Advisor signed statistics metadata gateway installed for this database.';
GO
