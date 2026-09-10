USE [SQLAdvisor];
GO

IF OBJECT_ID(N'ADM.ApplicationSetting', N'U') IS NULL
BEGIN
    CREATE TABLE ADM.ApplicationSetting
    (
        [Key] nvarchar(150) NOT NULL,
        [Value] nvarchar(4000) NULL,
        UpdatedAt datetimeoffset(3) NOT NULL
            CONSTRAINT DF_ADM_ApplicationSetting_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_ADM_ApplicationSetting PRIMARY KEY ([Key])
    );
END
GO

IF OBJECT_ID(N'QRY.WorkloadFile', N'U') IS NULL
BEGIN
    CREATE TABLE QRY.WorkloadFile
    (
        Id bigint IDENTITY(1,1) NOT NULL,
        ServerProfileId uniqueidentifier NULL,
        FilePath nvarchar(1500) NOT NULL,
        PathHash char(64) NOT NULL,
        FileName nvarchar(260) NOT NULL,
        DatabaseName nvarchar(128) NULL,
        ContentHash char(64) NOT NULL,
        NormalizedHash char(64) NOT NULL,
        QueryText nvarchar(max) NOT NULL,
        ReferencedObjects nvarchar(max) NOT NULL,
        PredicateColumns nvarchar(max) NOT NULL,
        LastWriteTimeUtc datetimeoffset(3) NOT NULL,
        LastScannedAt datetimeoffset(3) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_QRY_WorkloadFile_IsActive DEFAULT (1),
        ParseMessage nvarchar(2000) NULL,
        CONSTRAINT PK_QRY_WorkloadFile PRIMARY KEY (Id),
        CONSTRAINT UQ_QRY_WorkloadFile_PathHash UNIQUE (PathHash)
    );

    CREATE INDEX IX_QRY_WorkloadFile_ServerDatabase
        ON QRY.WorkloadFile(ServerProfileId, DatabaseName, IsActive);

    CREATE INDEX IX_QRY_WorkloadFile_LastScanned
        ON QRY.WorkloadFile(LastScannedAt DESC);
END
GO

MERGE ADM.ApplicationSetting AS target
USING (VALUES
    (N'Workload.Enabled', N'false'),
    (N'Workload.FolderPath', N''),
    (N'Workload.Recursive', N'false'),
    (N'Workload.DefaultServerProfileId', N''),
    (N'Workload.DefaultDatabaseName', N''),
    (N'Workload.ScanIntervalSeconds', N'300'),
    (N'Workload.MaxFileSizeKb', N'2048')
) AS source([Key], [Value])
ON target.[Key] = source.[Key]
WHEN NOT MATCHED THEN
    INSERT ([Key], [Value], UpdatedAt)
    VALUES (source.[Key], source.[Value], SYSUTCDATETIME());
GO

PRINT 'Workload file advisor schema ready.';
GO
