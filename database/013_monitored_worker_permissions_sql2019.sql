/*
  SQL Server Advisor - monitored server permissions for SQL Server 2019

  Run manually on EACH monitored SQL Server instance as sysadmin.
  This script grants read-only monitoring metadata/DMV permissions to the
  Windows service identity used by SQLServerAdvisorWorker.

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

DECLARE @WorkerLogin sysname = N'NT SERVICE\SQLServerAdvisorWorker';

IF SUSER_ID(@WorkerLogin) IS NULL
BEGIN
    DECLARE @createLogin nvarchar(max) =
        N'CREATE LOGIN ' + QUOTENAME(@WorkerLogin) + N' FROM WINDOWS;';
    EXEC sys.sp_executesql @createLogin;
END;
GO

GRANT VIEW SERVER STATE TO [NT SERVICE\SQLServerAdvisorWorker];
GRANT VIEW ANY DATABASE TO [NT SERVICE\SQLServerAdvisorWorker];
GO

DECLARE @sql nvarchar(max) = N'';

SELECT @sql += N'
USE ' + QUOTENAME(d.name) + N';
IF USER_ID(N''NT SERVICE\SQLServerAdvisorWorker'') IS NULL
BEGIN
    CREATE USER [NT SERVICE\SQLServerAdvisorWorker]
        FOR LOGIN [NT SERVICE\SQLServerAdvisorWorker];
END;
GRANT CONNECT TO [NT SERVICE\SQLServerAdvisorWorker];
GRANT VIEW DATABASE STATE TO [NT SERVICE\SQLServerAdvisorWorker];
GRANT VIEW DEFINITION TO [NT SERVICE\SQLServerAdvisorWorker];
'
FROM sys.databases AS d
WHERE d.database_id > 4
  AND d.state_desc = N'ONLINE'
  AND d.source_database_id IS NULL
  AND d.name <> N'SQLAdvisor';

EXEC sys.sp_executesql @sql;
GO

/* Effective-permission verification. Run this block after the grants. */
USE [master];
GO

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

EXECUTE AS LOGIN = N'NT SERVICE\SQLServerAdvisorWorker';

SELECT
    ORIGINAL_LOGIN() AS OriginalLogin,
    SUSER_SNAME() AS EffectiveLogin,
    CAST(HAS_PERMS_BY_NAME(NULL, N'SERVER', N'VIEW SERVER STATE') AS bit) AS HasViewServerState,
    CAST(HAS_PERMS_BY_NAME(NULL, N'SERVER', N'VIEW ANY DATABASE') AS bit) AS HasViewAnyDatabase;

DECLARE @checkSql nvarchar(max) = N'';
SELECT @checkSql += N'
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

EXEC sys.sp_executesql @checkSql;
REVERT;

SELECT *
FROM #AdvisorPermissionCheck
ORDER BY DatabaseName;
GO

PRINT N'SQL Server 2019 monitored-worker permissions and verification completed.';
GO
