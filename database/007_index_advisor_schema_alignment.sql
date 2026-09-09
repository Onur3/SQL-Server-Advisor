/* SQL Server Advisor - index advisor schema alignment
   SQL Server 2019+ compatible and idempotent. */

USE [SQLAdvisor];
GO

IF OBJECT_ID(N'SNP.[Index]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'SNP.[Index]', N'TypeDesc') IS NULL
        ALTER TABLE SNP.[Index] ADD TypeDesc nvarchar(60) NULL;

    IF COL_LENGTH(N'SNP.[Index]', N'IndexType') IS NOT NULL
        EXEC(N'UPDATE SNP.[Index] SET TypeDesc = COALESCE(TypeDesc, IndexType);');

    IF COL_LENGTH(N'SNP.[Index]', N'AvgFragmentationPercent') IS NULL
        ALTER TABLE SNP.[Index] ADD AvgFragmentationPercent decimal(9,3) NULL;

    IF COL_LENGTH(N'SNP.[Index]', N'PageCount') IS NULL
        ALTER TABLE SNP.[Index] ADD PageCount bigint NULL;

    UPDATE SNP.[Index]
    SET IndexName = COALESCE(IndexName, N'<unnamed>'),
        KeyColumns = COALESCE(KeyColumns, N''),
        IncludeColumns = COALESCE(IncludeColumns, N'');

    ALTER TABLE SNP.[Index] ALTER COLUMN IndexName nvarchar(512) NOT NULL;
    ALTER TABLE SNP.[Index] ALTER COLUMN KeyColumns nvarchar(max) NOT NULL;
    ALTER TABLE SNP.[Index] ALTER COLUMN IncludeColumns nvarchar(max) NOT NULL;
END
GO

IF OBJECT_ID(N'SNP.MissingIndex', N'U') IS NULL
BEGIN
    CREATE TABLE SNP.MissingIndex
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SNP_MissingIndex PRIMARY KEY,
        ServerProfileId uniqueidentifier NOT NULL,
        DatabaseName nvarchar(128) NOT NULL,
        TableName nvarchar(512) NOT NULL,
        EqualityColumns nvarchar(max) NOT NULL,
        InequalityColumns nvarchar(max) NOT NULL,
        IncludedColumns nvarchar(max) NOT NULL,
        UserSeeks bigint NOT NULL,
        UserScans bigint NOT NULL,
        AvgTotalUserCost decimal(19,3) NOT NULL,
        AvgUserImpact decimal(9,3) NOT NULL,
        ImprovementMeasure decimal(19,3) NOT NULL,
        CapturedAt datetimeoffset(3) NOT NULL,
        CONSTRAINT FK_SNP_MissingIndex_Server FOREIGN KEY(ServerProfileId) REFERENCES ADM.Server(Id)
    );
    CREATE INDEX IX_SNP_MissingIndex_ServerDatabaseCaptured
        ON SNP.MissingIndex(ServerProfileId, DatabaseName, CapturedAt DESC)
        INCLUDE(TableName, UserSeeks, UserScans, AvgUserImpact, ImprovementMeasure);
END
GO

PRINT N'Index advisor schema alignment ready.';
GO
