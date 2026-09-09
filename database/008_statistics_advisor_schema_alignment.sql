/* SQL Server Advisor - statistics advisor schema alignment
   SQL Server 2019+ compatible and idempotent. */

USE [SQLAdvisor];
GO

IF OBJECT_ID(N'SNP.[Statistics]', N'U') IS NOT NULL
AND EXISTS
(
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'SNP.[Statistics]')
      AND c.name = N'LastUpdated'
      AND t.name = N'datetimeoffset'
)
BEGIN
    ALTER TABLE SNP.[Statistics] ALTER COLUMN LastUpdated datetime2(3) NULL;
END
GO

IF OBJECT_ID(N'SNP.[Statistics]', N'U') IS NOT NULL
AND EXISTS
(
    SELECT 1
    FROM sys.columns c
    WHERE c.object_id = OBJECT_ID(N'SNP.[Statistics]')
      AND c.name = N'SamplePercent'
      AND (c.precision <> 9 OR c.scale <> 3)
)
BEGIN
    ALTER TABLE SNP.[Statistics] ALTER COLUMN SamplePercent decimal(9,3) NULL;
END
GO

PRINT N'Statistics advisor schema alignment ready.';
GO
