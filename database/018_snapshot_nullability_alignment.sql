/* SQL Server Advisor - snapshot nullability/schema alignment
   Repairs legacy nullable snapshot columns that are non-nullable in the .NET domain model.
   SQL Server 2019+ compatible and idempotent. */

USE [SQLAdvisor];
GO

IF OBJECT_ID(N'SNP.[Index]', N'U') IS NULL
    THROW 51000, 'SNP.Index table is missing. Run previous SQLAdvisor migrations first.', 1;
GO

/* Ensure all advanced IndexSnapshot columns exist even if an older 015 migration stopped midway. */
IF COL_LENGTH(N'SNP.[Index]', N'KeyDefinition') IS NULL
    ALTER TABLE SNP.[Index] ADD [KeyDefinition] nvarchar(max) NULL;
GO

IF COL_LENGTH(N'SNP.[Index]', N'FillFactor') IS NULL
    ALTER TABLE SNP.[Index] ADD [FillFactor] tinyint NOT NULL CONSTRAINT [DF_SNP_Index_FillFactor_018] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Index]', N'DataCompression') IS NULL
    ALTER TABLE SNP.[Index] ADD [DataCompression] nvarchar(60) NULL;
GO

IF COL_LENGTH(N'SNP.[Index]', N'UsageSinceDays') IS NULL
    ALTER TABLE SNP.[Index] ADD [UsageSinceDays] int NOT NULL CONSTRAINT [DF_SNP_Index_UsageSinceDays_018] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Index]', N'LeafInsertCount') IS NULL
    ALTER TABLE SNP.[Index] ADD [LeafInsertCount] bigint NOT NULL CONSTRAINT [DF_SNP_Index_LeafInsertCount_018] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Index]', N'LeafDeleteCount') IS NULL
    ALTER TABLE SNP.[Index] ADD [LeafDeleteCount] bigint NOT NULL CONSTRAINT [DF_SNP_Index_LeafDeleteCount_018] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Index]', N'LeafUpdateCount') IS NULL
    ALTER TABLE SNP.[Index] ADD [LeafUpdateCount] bigint NOT NULL CONSTRAINT [DF_SNP_Index_LeafUpdateCount_018] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Index]', N'LeafAllocationCount') IS NULL
    ALTER TABLE SNP.[Index] ADD [LeafAllocationCount] bigint NOT NULL CONSTRAINT [DF_SNP_Index_LeafAllocationCount_018] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Index]', N'RangeScanCount') IS NULL
    ALTER TABLE SNP.[Index] ADD [RangeScanCount] bigint NOT NULL CONSTRAINT [DF_SNP_Index_RangeScanCount_018] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Index]', N'SingletonLookupCount') IS NULL
    ALTER TABLE SNP.[Index] ADD [SingletonLookupCount] bigint NOT NULL CONSTRAINT [DF_SNP_Index_SingletonLookupCount_018] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Index]', N'PageLatchWaitMs') IS NULL
    ALTER TABLE SNP.[Index] ADD [PageLatchWaitMs] bigint NOT NULL CONSTRAINT [DF_SNP_Index_PageLatchWaitMs_018] DEFAULT (0);
GO

/* Repair legacy rows before enforcing the domain model's required scalar fields. */
IF COL_LENGTH(N'SNP.[Index]', N'TypeDesc') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'SNP.[Index]', N'IndexType') IS NOT NULL
        EXEC(N'UPDATE SNP.[Index] SET [TypeDesc] = COALESCE(NULLIF([TypeDesc], N''''), NULLIF([IndexType], N''''), N''<unknown>'') WHERE [TypeDesc] IS NULL OR [TypeDesc] = N'''';');
    ELSE
        UPDATE SNP.[Index] SET [TypeDesc] = N'<unknown>' WHERE [TypeDesc] IS NULL OR [TypeDesc] = N'';

    ALTER TABLE SNP.[Index] ALTER COLUMN [TypeDesc] nvarchar(60) NOT NULL;
END;
GO

UPDATE SNP.[Index]
SET [IndexName] = COALESCE([IndexName], N'<unnamed>'),
    [KeyColumns] = COALESCE([KeyColumns], N''),
    [IncludeColumns] = COALESCE([IncludeColumns], N''),
    [SizeMb] = COALESCE([SizeMb], 0),
    [UserSeeks] = COALESCE([UserSeeks], 0),
    [UserScans] = COALESCE([UserScans], 0),
    [UserLookups] = COALESCE([UserLookups], 0),
    [UserUpdates] = COALESCE([UserUpdates], 0);
GO

ALTER TABLE SNP.[Index] ALTER COLUMN [IndexName] nvarchar(512) NOT NULL;
ALTER TABLE SNP.[Index] ALTER COLUMN [KeyColumns] nvarchar(max) NOT NULL;
ALTER TABLE SNP.[Index] ALTER COLUMN [IncludeColumns] nvarchar(max) NOT NULL;
ALTER TABLE SNP.[Index] ALTER COLUMN [SizeMb] decimal(19,2) NOT NULL;
ALTER TABLE SNP.[Index] ALTER COLUMN [UserSeeks] bigint NOT NULL;
ALTER TABLE SNP.[Index] ALTER COLUMN [UserScans] bigint NOT NULL;
ALTER TABLE SNP.[Index] ALTER COLUMN [UserLookups] bigint NOT NULL;
ALTER TABLE SNP.[Index] ALTER COLUMN [UserUpdates] bigint NOT NULL;
GO

/* StatisticsSnapshot has the same legacy nullable/non-nullable mismatch. */
IF OBJECT_ID(N'SNP.[Statistics]', N'U') IS NOT NULL
BEGIN
    UPDATE SNP.[Statistics]
    SET [StatisticsName] = COALESCE([StatisticsName], N'<unnamed>'),
        [Rows] = COALESCE([Rows], 0),
        [RowsSampled] = COALESCE([RowsSampled], 0),
        [ModificationCounter] = COALESCE([ModificationCounter], 0);

    ALTER TABLE SNP.[Statistics] ALTER COLUMN [StatisticsName] nvarchar(512) NOT NULL;
    ALTER TABLE SNP.[Statistics] ALTER COLUMN [Rows] bigint NOT NULL;
    ALTER TABLE SNP.[Statistics] ALTER COLUMN [RowsSampled] bigint NOT NULL;
    ALTER TABLE SNP.[Statistics] ALTER COLUMN [ModificationCounter] bigint NOT NULL;
END;
GO

PRINT N'Snapshot nullability/schema alignment ready.';
GO
