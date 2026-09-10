/*
 SQL Server Advisor - SQL Server 2019 statistics metadata gateway TEMPLATE
 ------------------------------------------------------------------------
 MANUAL DBA ACTION. This filename contains "template" so deploy/install.ps1 will NOT run it.

 Purpose:
 - The Advisor monitoring login keeps NO direct SELECT permission on application tables.
 - A certificate user (WITHOUT LOGIN) owns the additional SELECT permission.
 - Only this signed stored procedure receives that certificate permission while executing.
 - The procedure returns statistics metadata only; it never returns application row data.

 Before running:
 1) Replace [CHANGE_ME_DATABASE] with one monitored database.
 2) Set @MonitoringLogin below to the ACTUAL login used by this ServerProfile:
      - Windows Authentication: normally NT SERVICE\SQLServerAdvisorWorker
      - SQL Login: exact ServerProfile.Username
 3) Replace CHANGE_ME_STRONG_CERTIFICATE_PASSWORD and keep that password securely.
 4) Run the monitored-principal permissions template first (database/013...).
 5) Re-run this template for every monitored database that needs Statistics Advisor coverage.

 IMPORTANT: CREATE OR ALTER PROCEDURE drops an existing module signature. Therefore this template
 recreates/updates the procedure and immediately signs it again.
*/

USE [CHANGE_ME_DATABASE];
GO

IF OBJECT_ID('tempdb..#SqlAdvisorMonitoringPrincipal') IS NOT NULL
    DROP TABLE #SqlAdvisorMonitoringPrincipal;

CREATE TABLE #SqlAdvisorMonitoringPrincipal
(
    MonitoringLogin sysname NOT NULL
);

DECLARE @MonitoringLogin sysname = N'NT SERVICE\SQLServerAdvisorWorker';
-- SQL Login kullanıyorsanız bu değeri ServerProfile.Username ile değiştirin.

IF SUSER_ID(@MonitoringLogin) IS NULL
BEGIN
    DECLARE @missingLoginMessage nvarchar(2048) =
        N'Monitoring login bulunamadı: ' + @MonitoringLogin + N'. Önce doğru ServerProfile login adını ve 013 izin template''ini kontrol edin.';
    THROW 51000, @missingLoginMessage, 1;
END;

IF USER_ID(@MonitoringLogin) IS NULL
BEGIN
    DECLARE @createUserSql nvarchar(max) =
        N'CREATE USER ' + QUOTENAME(@MonitoringLogin) + N' FOR LOGIN ' + QUOTENAME(@MonitoringLogin) + N';';
    EXEC sys.sp_executesql @createUserSql;
END;

INSERT #SqlAdvisorMonitoringPrincipal(MonitoringLogin) VALUES (@MonitoringLogin);

PRINT N'Statistics gateway monitoring login: ' + @MonitoringLogin;
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

DECLARE @MonitoringLogin sysname =
    (SELECT TOP (1) MonitoringLogin FROM #SqlAdvisorMonitoringPrincipal);

IF @MonitoringLogin IS NULL
    THROW 51000, 'Statistics gateway monitoring principal context is missing.', 1;

DECLARE @grantExecuteSql nvarchar(max) =
    N'GRANT EXECUTE ON OBJECT::dbo.usp_SQLAdvisor_StatisticsMetadata TO ' + QUOTENAME(@MonitoringLogin) + N';';
EXEC sys.sp_executesql @grantExecuteSql;

/* Effective-access verification: signed procedure should succeed under the actual monitoring user. */
DECLARE @verifySql nvarchar(max) =
    N'EXECUTE AS USER = ' + QUOTENAME(@MonitoringLogin, '''') + N';
EXEC dbo.usp_SQLAdvisor_StatisticsMetadata;
REVERT;';
EXEC sys.sp_executesql @verifySql;

DROP TABLE #SqlAdvisorMonitoringPrincipal;
GO

PRINT N'SQL Server Advisor signed statistics metadata gateway installed for this database.';
GO
