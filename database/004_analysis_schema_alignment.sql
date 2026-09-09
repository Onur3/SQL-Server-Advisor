/* SQL Server Advisor - analysis schema alignment
   SQL Server 2019+ compatible and idempotent. */

USE [SQLAdvisor];
GO

IF COL_LENGTH(N'ANL.Finding', N'QueryId') IS NULL
    ALTER TABLE ANL.Finding ADD QueryId bigint NULL;
GO

IF COL_LENGTH(N'ANL.Finding', N'DatabaseName') IS NULL
    ALTER TABLE ANL.Finding ADD DatabaseName sysname NULL;
GO

IF COL_LENGTH(N'ANL.Finding', N'ObjectName') IS NULL
    ALTER TABLE ANL.Finding ADD ObjectName nvarchar(512) NULL;
GO

IF COL_LENGTH(N'ANL.Finding', N'ResolvedAt') IS NULL
    ALTER TABLE ANL.Finding ADD ResolvedAt datetimeoffset(3) NULL;
GO

IF COL_LENGTH(N'REC.Recommendation', N'ReviewedAt') IS NULL
    ALTER TABLE REC.Recommendation ADD ReviewedAt datetimeoffset(3) NULL;
GO

IF COL_LENGTH(N'REC.Recommendation', N'ImplementedAt') IS NULL
    ALTER TABLE REC.Recommendation ADD ImplementedAt datetimeoffset(3) NULL;
GO

IF COL_LENGTH(N'REC.Recommendation', N'VerifiedAt') IS NULL
    ALTER TABLE REC.Recommendation ADD VerifiedAt datetimeoffset(3) NULL;
GO

IF COL_LENGTH(N'REC.Recommendation', N'WorkflowNote') IS NULL
    ALTER TABLE REC.Recommendation ADD WorkflowNote nvarchar(4000) NULL;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'REC.Recommendation')
      AND name = N'UX_REC_Recommendation_FindingId'
)
AND NOT EXISTS
(
    SELECT FindingId
    FROM REC.Recommendation
    GROUP BY FindingId
    HAVING COUNT(*) > 1
)
BEGIN
    CREATE UNIQUE INDEX UX_REC_Recommendation_FindingId
        ON REC.Recommendation(FindingId);
END
GO

PRINT N'Analysis schema alignment ready.';
GO
