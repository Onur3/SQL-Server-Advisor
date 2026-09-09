/* SQL Server Advisor - index/statistics engine v2 schema alignment
   SQL Server 2019+ compatible and idempotent. */

USE [SQLAdvisor];
GO

IF COL_LENGTH(N'SNP.[Index]', N'UsageSinceDays') IS NULL
BEGIN
    ALTER TABLE SNP.[Index]
        ADD UsageSinceDays int NOT NULL
            CONSTRAINT DF_SNP_Index_UsageSinceDays DEFAULT (0);
END
GO

IF COL_LENGTH(N'SNP.[Index]', N'HasFilter') IS NULL
BEGIN
    ALTER TABLE SNP.[Index]
        ADD HasFilter bit NOT NULL
            CONSTRAINT DF_SNP_Index_HasFilter DEFAULT (0);
END
GO

IF COL_LENGTH(N'SNP.[Index]', N'FilterDefinition') IS NULL
BEGIN
    ALTER TABLE SNP.[Index]
        ADD FilterDefinition nvarchar(max) NULL;
END
GO

IF COL_LENGTH(N'SNP.[Statistics]', N'AutoCreated') IS NULL
BEGIN
    ALTER TABLE SNP.[Statistics]
        ADD AutoCreated bit NOT NULL
            CONSTRAINT DF_SNP_Statistics_AutoCreated DEFAULT (0);
END
GO

IF COL_LENGTH(N'SNP.[Statistics]', N'UserCreated') IS NULL
BEGIN
    ALTER TABLE SNP.[Statistics]
        ADD UserCreated bit NOT NULL
            CONSTRAINT DF_SNP_Statistics_UserCreated DEFAULT (0);
END
GO

IF COL_LENGTH(N'SNP.[Statistics]', N'NoRecompute') IS NULL
BEGIN
    ALTER TABLE SNP.[Statistics]
        ADD NoRecompute bit NOT NULL
            CONSTRAINT DF_SNP_Statistics_NoRecompute DEFAULT (0);
END
GO

IF COL_LENGTH(N'SNP.[Statistics]', N'HasFilter') IS NULL
BEGIN
    ALTER TABLE SNP.[Statistics]
        ADD HasFilter bit NOT NULL
            CONSTRAINT DF_SNP_Statistics_HasFilter DEFAULT (0);
END
GO

IF COL_LENGTH(N'SNP.[Statistics]', N'FilterDefinition') IS NULL
BEGIN
    ALTER TABLE SNP.[Statistics]
        ADD FilterDefinition nvarchar(max) NULL;
END
GO

PRINT N'Index and statistics engine v2 schema alignment ready.';
GO
