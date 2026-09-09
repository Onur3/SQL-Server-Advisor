/* SQL Server Advisor - query performance schema alignment
   SQL Server 2019+ compatible and idempotent.
   Legacy QRY.QueryPlan / QRY.QueryRuntime tables are preserved. */

USE [SQLAdvisor];
GO

/* Align legacy binary query hashes with the current EF string model. */
IF OBJECT_ID(N'QRY.Query', N'U') IS NOT NULL
AND EXISTS
(
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'QRY.Query')
      AND c.name = N'QueryHash'
      AND t.name IN (N'binary', N'varbinary')
)
AND COL_LENGTH(N'QRY.Query', N'QueryHashText') IS NULL
BEGIN
    ALTER TABLE QRY.Query ADD QueryHashText nvarchar(128) NULL;
END
GO

IF OBJECT_ID(N'QRY.Query', N'U') IS NOT NULL
AND COL_LENGTH(N'QRY.Query', N'QueryHashText') IS NOT NULL
AND EXISTS
(
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'QRY.Query')
      AND c.name = N'QueryHash'
      AND t.name IN (N'binary', N'varbinary')
)
BEGIN
    EXEC sys.sp_executesql N'
        UPDATE QRY.Query
        SET QueryHashText = COALESCE(CONVERT(varchar(130), QueryHash, 1), CONCAT(N''legacy-'', Id))
        WHERE QueryHashText IS NULL;';

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'QRY.Query') AND name = N'IX_QRY_Query_ServerDatabaseHash')
        DROP INDEX IX_QRY_Query_ServerDatabaseHash ON QRY.Query;

    EXEC sys.sp_executesql N'ALTER TABLE QRY.Query DROP COLUMN QueryHash;';
    EXEC sys.sp_rename N'QRY.Query.QueryHashText', N'QueryHash', N'COLUMN';
    EXEC sys.sp_executesql N'ALTER TABLE QRY.Query ALTER COLUMN QueryHash nvarchar(128) NOT NULL;';
END
GO

IF OBJECT_ID(N'QRY.Query', N'U') IS NOT NULL
AND EXISTS
(
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'QRY.Query')
      AND c.name = N'NormalizedHash'
      AND t.name IN (N'binary', N'varbinary')
)
AND COL_LENGTH(N'QRY.Query', N'NormalizedHashText') IS NULL
BEGIN
    ALTER TABLE QRY.Query ADD NormalizedHashText nvarchar(128) NULL;
END
GO

IF OBJECT_ID(N'QRY.Query', N'U') IS NOT NULL
AND COL_LENGTH(N'QRY.Query', N'NormalizedHashText') IS NOT NULL
AND EXISTS
(
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'QRY.Query')
      AND c.name = N'NormalizedHash'
      AND t.name IN (N'binary', N'varbinary')
)
BEGIN
    EXEC sys.sp_executesql N'
        UPDATE QRY.Query
        SET NormalizedHashText = COALESCE(CONVERT(varchar(130), NormalizedHash, 1), QueryHash)
        WHERE NormalizedHashText IS NULL;';

    EXEC sys.sp_executesql N'ALTER TABLE QRY.Query DROP COLUMN NormalizedHash;';
    EXEC sys.sp_rename N'QRY.Query.NormalizedHashText', N'NormalizedHash', N'COLUMN';
    EXEC sys.sp_executesql N'ALTER TABLE QRY.Query ALTER COLUMN NormalizedHash nvarchar(128) NOT NULL;';
END
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'QRY.Query')
      AND name = N'UX_QRY_Query_ServerDatabaseHash'
)
AND NOT EXISTS
(
    SELECT ServerProfileId, DatabaseName, QueryHash
    FROM QRY.Query
    GROUP BY ServerProfileId, DatabaseName, QueryHash
    HAVING COUNT(*) > 1
)
BEGIN
    CREATE UNIQUE INDEX UX_QRY_Query_ServerDatabaseHash
        ON QRY.Query(ServerProfileId, DatabaseName, QueryHash);
END
GO

IF OBJECT_ID(N'QRY.[Plan]', N'U') IS NULL
BEGIN
    CREATE TABLE QRY.[Plan]
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_QRY_Plan PRIMARY KEY,
        QueryId bigint NOT NULL,
        PlanHash nvarchar(128) NOT NULL,
        Source nvarchar(30) NOT NULL,
        HasActualRuntimeCounters bit NOT NULL CONSTRAINT DF_QRY_Plan_ActualRuntime DEFAULT(0),
        PlanXml nvarchar(max) NOT NULL,
        FirstSeenAt datetimeoffset(3) NOT NULL,
        LastSeenAt datetimeoffset(3) NOT NULL,
        CONSTRAINT FK_QRY_Plan_Query FOREIGN KEY(QueryId) REFERENCES QRY.Query(Id)
    );
    CREATE UNIQUE INDEX UX_QRY_Plan_QueryHashSource ON QRY.[Plan](QueryId, PlanHash, Source);
END
GO

IF OBJECT_ID(N'QRY.Runtime', N'U') IS NULL
BEGIN
    CREATE TABLE QRY.Runtime
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_QRY_Runtime PRIMARY KEY,
        ServerProfileId uniqueidentifier NOT NULL,
        QueryId bigint NOT NULL,
        PlanId bigint NULL,
        Source nvarchar(30) NOT NULL,
        ExecutionCount bigint NOT NULL,
        TotalCpuMs decimal(19,3) NOT NULL,
        AverageCpuMs decimal(19,3) NOT NULL,
        TotalDurationMs decimal(19,3) NOT NULL,
        AverageDurationMs decimal(19,3) NOT NULL,
        TotalLogicalReads bigint NOT NULL,
        AverageLogicalReads decimal(19,3) NOT NULL,
        TotalLogicalWrites bigint NOT NULL,
        ImpactScore decimal(6,2) NOT NULL,
        LastExecutionTime datetimeoffset(3) NULL,
        CapturedAt datetimeoffset(3) NOT NULL,
        CONSTRAINT FK_QRY_Runtime_Server FOREIGN KEY(ServerProfileId) REFERENCES ADM.Server(Id),
        CONSTRAINT FK_QRY_Runtime_Query FOREIGN KEY(QueryId) REFERENCES QRY.Query(Id),
        CONSTRAINT FK_QRY_Runtime_Plan FOREIGN KEY(PlanId) REFERENCES QRY.[Plan](Id)
    );
    CREATE INDEX IX_QRY_Runtime_ServerQueryCaptured ON QRY.Runtime(ServerProfileId, QueryId, CapturedAt DESC)
        INCLUDE(ImpactScore, AverageCpuMs, AverageDurationMs, AverageLogicalReads, ExecutionCount);
    CREATE INDEX IX_QRY_Runtime_Impact ON QRY.Runtime(ServerProfileId, CapturedAt DESC, ImpactScore DESC);
END
GO

PRINT N'Query performance schema alignment ready.';
GO
