/* SQL Server Advisor - advanced index/statistics tuning engine schema alignment
   SQL Server 2019+ compatible and idempotent. */

USE [SQLAdvisor];
GO

IF COL_LENGTH(N'SNP.[Index]', N'KeyDefinition') IS NULL
    ALTER TABLE SNP.[Index] ADD [KeyDefinition] nvarchar(max) NULL;
GO

IF COL_LENGTH(N'SNP.[Index]', N'FillFactor') IS NULL
    ALTER TABLE SNP.[Index] ADD [FillFactor] tinyint NOT NULL CONSTRAINT [DF_SNP_Index_FillFactor] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Index]', N'DataCompression') IS NULL
    ALTER TABLE SNP.[Index] ADD [DataCompression] nvarchar(60) NULL;
GO

IF COL_LENGTH(N'SNP.[Index]', N'LeafInsertCount') IS NULL
    ALTER TABLE SNP.[Index] ADD [LeafInsertCount] bigint NOT NULL CONSTRAINT [DF_SNP_Index_LeafInsertCount] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Index]', N'LeafDeleteCount') IS NULL
    ALTER TABLE SNP.[Index] ADD [LeafDeleteCount] bigint NOT NULL CONSTRAINT [DF_SNP_Index_LeafDeleteCount] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Index]', N'LeafUpdateCount') IS NULL
    ALTER TABLE SNP.[Index] ADD [LeafUpdateCount] bigint NOT NULL CONSTRAINT [DF_SNP_Index_LeafUpdateCount] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Index]', N'LeafAllocationCount') IS NULL
    ALTER TABLE SNP.[Index] ADD [LeafAllocationCount] bigint NOT NULL CONSTRAINT [DF_SNP_Index_LeafAllocationCount] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Index]', N'RangeScanCount') IS NULL
    ALTER TABLE SNP.[Index] ADD [RangeScanCount] bigint NOT NULL CONSTRAINT [DF_SNP_Index_RangeScanCount] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Index]', N'SingletonLookupCount') IS NULL
    ALTER TABLE SNP.[Index] ADD [SingletonLookupCount] bigint NOT NULL CONSTRAINT [DF_SNP_Index_SingletonLookupCount] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Index]', N'PageLatchWaitMs') IS NULL
    ALTER TABLE SNP.[Index] ADD [PageLatchWaitMs] bigint NOT NULL CONSTRAINT [DF_SNP_Index_PageLatchWaitMs] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Statistics]', N'StatisticsColumns') IS NULL
    ALTER TABLE SNP.[Statistics] ADD [StatisticsColumns] nvarchar(max) NULL;
GO

IF COL_LENGTH(N'SNP.[Statistics]', N'Steps') IS NULL
    ALTER TABLE SNP.[Statistics] ADD [Steps] int NULL;
GO

IF COL_LENGTH(N'SNP.[Statistics]', N'UnfilteredRows') IS NULL
    ALTER TABLE SNP.[Statistics] ADD [UnfilteredRows] bigint NULL;
GO

IF COL_LENGTH(N'SNP.[Statistics]', N'PersistedSamplePercent') IS NULL
    ALTER TABLE SNP.[Statistics] ADD [PersistedSamplePercent] decimal(9,3) NULL;
GO

IF COL_LENGTH(N'SNP.[Statistics]', N'IsIncremental') IS NULL
    ALTER TABLE SNP.[Statistics] ADD [IsIncremental] bit NOT NULL CONSTRAINT [DF_SNP_Statistics_IsIncremental] DEFAULT (0);
GO

IF COL_LENGTH(N'SNP.[Statistics]', N'PropertiesVisible') IS NULL
    ALTER TABLE SNP.[Statistics] ADD [PropertiesVisible] bit NOT NULL CONSTRAINT [DF_SNP_Statistics_PropertiesVisible] DEFAULT (1);
GO

PRINT N'Advanced index and statistics tuning engine schema alignment ready.';
GO
