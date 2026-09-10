/*
  SQL Server Advisor - monitored principal permissions for SQL Server 2019

  Run manually on EACH monitored SQL Server instance as sysadmin.
  This script grants read-only monitoring metadata/DMV permissions to the
  ACTUAL login used by the monitored ServerProfile connection.

  IMPORTANT - choose the correct principal below:
    - Windows Authentication profile: normally NT SERVICE\SQLServerAdvisorWorker
    - SQL Login profile: use the exact ServerProfile.Username value

  The Index Advisor warning reports SUSER_SNAME() so the effective login can be
  matched to this script without guessing.

  TEMPLATE suffix is intentional: deploy/install.ps1 excludes *template*.sql
  files from automatic SQLAdvisor migrations. These grants must remain an
  explicit DBA action on the monitored instance.

  It intentionally DOES NOT grant:
    SELECT / INSERT / UPDATE / DELETE on application tables
    ALTER / CONTROL / db_owner / db_ddladmin
    CREATE INDEX / DROP INDEX / UPDATE STATISTICS / KILL rights

  Important Statistics Advisor note:
  sys.dm_db_stats_properties has additional object/column permission rules.
  The grants below are sufficient for the Index Advisor and the SQL Server 2019
  server/database-state DMVs used by the core collectors, but they intentionally
  do not broaden user-data permissions merely to expose statistics properties.
*/

USE [master];
GO

IF OBJECT_ID('tempdb..#SqlAdvisorMonitoringPrincipal') IS NOT NULL
    DROP TABLE #SqlAdvisorMonitoringPrincipal;

CREATE TABLE #SqlAdvisorMonitoringPrincipal
(
    MonitoringLogin sysname NOT NULL
);

/* CHANGE ONLY THIS VALUE when the ServerProfile uses SQL Login authentication. */
DECLARE @MonitoringLogin sysname = N'NT SERVICE\SQLServerAdvisorWorker';

IF SUSER_ID(@MonitoringLogin) IS NULL
BEGIN
    IF @MonitoringLogin = N'NT SERVICE\SQLServerAdvisorWorker'
    BEGIN
        DECLARE @createLogin nvarchar(max) =
            N'CREATE LOGIN ' + QUOTENAME(@MonitoringLogin) + N' FROM WINDOWS;';
        EXEC sys.sp_executesql @createLogin;
    END
    ELSE
    BEGIN
        DECLARE @missingLoginMessage nvarchar(2048) =
            N'Monitoring login bulunamadı: ' + @MonitoringLogin +
            N'. SQL Login profili kullanıyorsanız login önceden mevcut olmalı ve ad ServerProfile.Username ile birebir aynı olmalıdır.';
        THROW 51000, @missingLoginMessage, 1;
    END
END;

INSERT #SqlAdvisorMonitoringPrincipal(MonitoringLogin) VALUES (@MonitoringLogin);

DECLARE @serverGrantSql nvarchar(max) =
    N'GRANT VIEW SERVER STATE TO ' + QUOTENAME(@MonitoringLogin) + N';' + CHAR(13) + CHAR(10) +
    N'GRANT VIEW ANY DATABASE TO ' + QUOTENAME(@MonitoringLogin) + N';';
EXEC sys.sp_executesql @serverGrantSql;

PRINT N'Monitoring login: ' + @MonitoringLogin;
PRINT N'Server permissions granted: VIEW SERVER STATE, VIEW ANY DATABASE.';
GO

DECLARE @MonitoringLogin sysname =
    (SELECT TOP (1) MonitoringLogin FROM #SqlAdvisorMonitoringPrincipal);

IF @MonitoringLogin IS NULL
    THROW 51000, 'Monitoring principal context is missing.', 1;

DECLARE @sql nvarchar(max) = N'';

SELECT @sql += N'
USE ' + QUOTENAME(d.name) + N';
IF USER_ID(N' + QUOTENAME(@MonitoringLogin, '''') + N') IS NULL
BEGIN
    CREATE USER ' + QUOTENAME(@MonitoringLogin) + N'
        FOR LOGIN ' + QUOTENAME(@MonitoringLogin) + N';
END;
GRANT CONNECT TO ' + QUOTENAME(@MonitoringLogin) + N';
GRANT VIEW DATABASE STATE TO ' + QUOTENAME(@MonitoringLogin) + N';
GRANT VIEW DEFINITION TO ' + QUOTENAME(@MonitoringLogin) + N';
'
FROM sys.databases AS d
WHERE d.database_id > 4
  AND d.state_desc = N'ONLINE'
  AND d.source_database_id IS NULL
  AND d.name <> N'SQLAdvisor';

EXEC sys.sp_executesql @sql;
GO

/* Effective-permission verification. */
USE [master];
GO

DECLARE @MonitoringLogin sysname =
    (SELECT TOP (1) MonitoringLogin FROM #SqlAdvisorMonitoringPrincipal);

IF @MonitoringLogin IS NULL
    THROW 51000, 'Monitoring principal context is missing.', 1;

IF OBJECT_ID('tempdb..#AdvisorPermissionCheck') IS NOT NULL
    DROP TABLE #AdvisorPermissionCheck;

CREATE TABLE #AdvisorPermissionCheck
(
    DatabaseName sysname NOT NULL,
    HasDatabaseAccess bit NOT NULL,
    HasConnect bit NOT NULL,
    HasViewDatabaseState bit NOT NULL,
    HasViewDefinition bit NOT NULL
);

DECLARE @verifySql nvarchar(max) =
    N'EXECUTE AS LOGIN = ' + QUOTENAME(@MonitoringLogin, '''') + N';
SELECT
    ORIGINAL_LOGIN() AS OriginalLogin,
    SUSER_SNAME() AS EffectiveLogin,
    CAST(HAS_PERMS_BY_NAME(NULL, N''SERVER'', N''VIEW SERVER STATE'') AS bit) AS HasViewServerState,
    CAST(HAS_PERMS_BY_NAME(NULL, N''SERVER'', N''VIEW ANY DATABASE'') AS bit) AS HasViewAnyDatabase;
';

SELECT @verifySql += N'
USE ' + QUOTENAME(d.name) + N';
INSERT #AdvisorPermissionCheck(DatabaseName, HasDatabaseAccess, HasConnect, HasViewDatabaseState, HasViewDefinition)
SELECT
    DB_NAME(),
    CAST(CASE WHEN HAS_DBACCESS(DB_NAME()) = 1 THEN 1 ELSE 0 END AS bit),
    CAST(HAS_PERMS_BY_NAME(DB_NAME(), N''DATABASE'', N''CONNECT'') AS bit),
    CAST(HAS_PERMS_BY_NAME(DB_NAME(), N''DATABASE'', N''VIEW DATABASE STATE'') AS bit),
    CAST(HAS_PERMS_BY_NAME(DB_NAME(), N''DATABASE'', N''VIEW DEFINITION'') AS bit);
'
FROM master.sys.databases AS d
WHERE d.database_id > 4
  AND d.state_desc = N'ONLINE'
  AND d.source_database_id IS NULL
  AND d.name <> N'SQLAdvisor';

SET @verifySql += N'
USE [master];
REVERT;';

EXEC sys.sp_executesql @verifySql;

SELECT *
FROM #AdvisorPermissionCheck
ORDER BY DatabaseName;

DROP TABLE #AdvisorPermissionCheck;
DROP TABLE #SqlAdvisorMonitoringPrincipal;
GO

PRINT N'SQL Server 2019 monitored-principal permissions and verification completed.';
GO
