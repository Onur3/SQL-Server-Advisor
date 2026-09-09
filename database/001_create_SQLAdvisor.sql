/* SQL Server Advisor - Application database
   Target: SQL Server 2025 (17.x)
   This database stores monitoring history and recommendations.
   It is NOT the monitored production database. */

USE [master];
GO

IF DB_ID(N'SQLAdvisor') IS NULL
BEGIN
    CREATE DATABASE [SQLAdvisor];
END
GO

ALTER DATABASE [SQLAdvisor] SET RECOVERY SIMPLE;
ALTER DATABASE [SQLAdvisor] SET AUTO_CLOSE OFF;
ALTER DATABASE [SQLAdvisor] SET AUTO_SHRINK OFF;
ALTER DATABASE [SQLAdvisor] SET PAGE_VERIFY CHECKSUM;
ALTER DATABASE [SQLAdvisor] SET AUTO_CREATE_STATISTICS ON;
ALTER DATABASE [SQLAdvisor] SET AUTO_UPDATE_STATISTICS ON;
GO

USE [SQLAdvisor];
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'ADM') EXEC(N'CREATE SCHEMA ADM AUTHORIZATION dbo;');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'COL') EXEC(N'CREATE SCHEMA COL AUTHORIZATION dbo;');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'SNP') EXEC(N'CREATE SCHEMA SNP AUTHORIZATION dbo;');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'QRY') EXEC(N'CREATE SCHEMA QRY AUTHORIZATION dbo;');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'EVT') EXEC(N'CREATE SCHEMA EVT AUTHORIZATION dbo;');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'ANL') EXEC(N'CREATE SCHEMA ANL AUTHORIZATION dbo;');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'REC') EXEC(N'CREATE SCHEMA REC AUTHORIZATION dbo;');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'BLN') EXEC(N'CREATE SCHEMA BLN AUTHORIZATION dbo;');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'AGG') EXEC(N'CREATE SCHEMA AGG AUTHORIZATION dbo;');
GO

IF OBJECT_ID(N'ADM.Server', N'U') IS NULL
BEGIN
    CREATE TABLE ADM.Server
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ADM_Server PRIMARY KEY,
        Name nvarchar(150) NOT NULL,
        Host nvarchar(255) NOT NULL,
        Port int NOT NULL CONSTRAINT DF_ADM_Server_Port DEFAULT (1433),
        DefaultDatabase nvarchar(128) NOT NULL CONSTRAINT DF_ADM_Server_DefaultDatabase DEFAULT (N'master'),
        AuthenticationType int NOT NULL,
        Username nvarchar(256) NULL,
        ProtectedPassword nvarchar(max) NULL,
        Encrypt bit NOT NULL CONSTRAINT DF_ADM_Server_Encrypt DEFAULT (1),
        TrustServerCertificate bit NOT NULL CONSTRAINT DF_ADM_Server_TrustServerCertificate DEFAULT (0),
        IsEnabled bit NOT NULL CONSTRAINT DF_ADM_Server_IsEnabled DEFAULT (1),
        CreatedAt datetimeoffset(3) NOT NULL CONSTRAINT DF_ADM_Server_CreatedAt DEFAULT (SYSUTCDATETIME()),
        LastConnectedAt datetimeoffset(3) NULL,
        LastError nvarchar(2000) NULL,
        CONSTRAINT UQ_ADM_Server_Name UNIQUE (Name)
    );
END
GO

IF OBJECT_ID(N'COL.WorkerHeartbeat', N'U') IS NULL
BEGIN
    CREATE TABLE COL.WorkerHeartbeat
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_COL_WorkerHeartbeat PRIMARY KEY,
        MachineName nvarchar(255) NOT NULL,
        ProcessId int NOT NULL,
        Version nvarchar(50) NOT NULL,
        StartedAt datetimeoffset(3) NOT NULL,
        LastHeartbeatAt datetimeoffset(3) NOT NULL,
        Status nvarchar(30) NOT NULL
    );
    CREATE INDEX IX_COL_WorkerHeartbeat_LastHeartbeatAt ON COL.WorkerHeartbeat(LastHeartbeatAt DESC);
END
GO

IF OBJECT_ID(N'COL.CollectorRun', N'U') IS NULL
BEGIN
    CREATE TABLE COL.CollectorRun
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_COL_CollectorRun PRIMARY KEY,
        ServerProfileId uniqueidentifier NOT NULL,
        CollectorType nvarchar(100) NOT NULL,
        StartedAt datetimeoffset(3) NOT NULL,
        CompletedAt datetimeoffset(3) NULL,
        Status nvarchar(30) NOT NULL,
        RowsCollected int NOT NULL CONSTRAINT DF_COL_CollectorRun_Rows DEFAULT (0),
        DurationMs bigint NULL,
        ErrorMessage nvarchar(4000) NULL,
        CONSTRAINT FK_COL_CollectorRun_Server FOREIGN KEY(ServerProfileId) REFERENCES ADM.Server(Id)
    );
    CREATE INDEX IX_COL_CollectorRun_ServerStarted ON COL.CollectorRun(ServerProfileId, StartedAt DESC) INCLUDE(CollectorType, Status, DurationMs);
END
GO

IF OBJECT_ID(N'SNP.Server', N'U') IS NULL
BEGIN
    CREATE TABLE SNP.Server
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SNP_Server PRIMARY KEY,
        ServerProfileId uniqueidentifier NOT NULL,
        CapturedAt datetimeoffset(3) NOT NULL,
        ServerName nvarchar(255) NULL,
        ProductVersion nvarchar(100) NULL,
        Edition nvarchar(255) NULL,
        SqlServerStartTime datetimeoffset(3) NULL,
        SqlCpuPercent int NULL,
        SystemIdlePercent int NULL,
        PhysicalMemoryMb bigint NULL,
        AvailableMemoryMb bigint NULL,
        SqlMemoryMb bigint NULL,
        PageLifeExpectancy bigint NULL,
        ActiveSessions int NOT NULL CONSTRAINT DF_SNP_Server_ActiveSessions DEFAULT(0),
        ActiveRequests int NOT NULL CONSTRAINT DF_SNP_Server_ActiveRequests DEFAULT(0),
        BlockedRequests int NOT NULL CONSTRAINT DF_SNP_Server_BlockedRequests DEFAULT(0),
        UserConnections int NOT NULL CONSTRAINT DF_SNP_Server_UserConnections DEFAULT(0),
        SessionMetricsAvailable bit NOT NULL CONSTRAINT DF_SNP_Server_SessionMetrics DEFAULT(0),
        MemoryMetricsAvailable bit NOT NULL CONSTRAINT DF_SNP_Server_MemoryMetrics DEFAULT(0),
        CpuMetricsAvailable bit NOT NULL CONSTRAINT DF_SNP_Server_CpuMetrics DEFAULT(0),
        CONSTRAINT FK_SNP_Server_Server FOREIGN KEY(ServerProfileId) REFERENCES ADM.Server(Id)
    );
    CREATE INDEX IX_SNP_Server_ServerCaptured ON SNP.Server(ServerProfileId, CapturedAt DESC)
        INCLUDE(SqlCpuPercent, AvailableMemoryMb, ActiveSessions, ActiveRequests, BlockedRequests, UserConnections);
END
GO

IF OBJECT_ID(N'QRY.Query', N'U') IS NULL
BEGIN
    CREATE TABLE QRY.Query
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_QRY_Query PRIMARY KEY,
        ServerProfileId uniqueidentifier NOT NULL,
        DatabaseName sysname NOT NULL,
        QueryHash binary(8) NULL,
        NormalizedHash binary(32) NULL,
        ObjectId int NULL,
        ObjectName nvarchar(512) NULL,
        StatementText nvarchar(max) NOT NULL,
        FirstSeenAt datetimeoffset(3) NOT NULL,
        LastSeenAt datetimeoffset(3) NOT NULL,
        CONSTRAINT FK_QRY_Query_Server FOREIGN KEY(ServerProfileId) REFERENCES ADM.Server(Id)
    );
    CREATE INDEX IX_QRY_Query_ServerDatabaseHash ON QRY.Query(ServerProfileId, DatabaseName, QueryHash);
END
GO

IF OBJECT_ID(N'QRY.QueryPlan', N'U') IS NULL
BEGIN
    CREATE TABLE QRY.QueryPlan
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_QRY_QueryPlan PRIMARY KEY,
        QueryId bigint NOT NULL,
        PlanHash binary(32) NOT NULL,
        PlanType tinyint NOT NULL CONSTRAINT DF_QRY_QueryPlan_Type DEFAULT(0),
        PlanXml varbinary(max) NOT NULL,
        IsCompressed bit NOT NULL CONSTRAINT DF_QRY_QueryPlan_Compressed DEFAULT(1),
        FirstSeenAt datetimeoffset(3) NOT NULL,
        LastSeenAt datetimeoffset(3) NOT NULL,
        CONSTRAINT FK_QRY_QueryPlan_Query FOREIGN KEY(QueryId) REFERENCES QRY.Query(Id),
        CONSTRAINT UQ_QRY_QueryPlan UNIQUE(QueryId, PlanHash)
    );
END
GO

IF OBJECT_ID(N'QRY.QueryRuntime', N'U') IS NULL
BEGIN
    CREATE TABLE QRY.QueryRuntime
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_QRY_QueryRuntime PRIMARY KEY,
        QueryId bigint NOT NULL,
        QueryPlanId bigint NULL,
        CapturedAt datetimeoffset(3) NOT NULL,
        ExecutionCount bigint NOT NULL,
        TotalCpuMs bigint NULL,
        AverageCpuMs decimal(19,3) NULL,
        TotalDurationMs bigint NULL,
        AverageDurationMs decimal(19,3) NULL,
        TotalLogicalReads bigint NULL,
        AverageLogicalReads decimal(19,3) NULL,
        TotalLogicalWrites bigint NULL,
        MinDurationMs decimal(19,3) NULL,
        MaxDurationMs decimal(19,3) NULL,
        LastExecutionTime datetimeoffset(3) NULL,
        MemoryGrantKb bigint NULL,
        SpillLevel int NULL,
        CONSTRAINT FK_QRY_QueryRuntime_Query FOREIGN KEY(QueryId) REFERENCES QRY.Query(Id),
        CONSTRAINT FK_QRY_QueryRuntime_Plan FOREIGN KEY(QueryPlanId) REFERENCES QRY.QueryPlan(Id)
    );
    CREATE INDEX IX_QRY_QueryRuntime_QueryCaptured ON QRY.QueryRuntime(QueryId, CapturedAt DESC);
END
GO

IF OBJECT_ID(N'SNP.[Index]', N'U') IS NULL
BEGIN
    CREATE TABLE SNP.[Index]
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SNP_Index PRIMARY KEY,
        ServerProfileId uniqueidentifier NOT NULL,
        DatabaseName sysname NOT NULL,
        ObjectId int NOT NULL,
        IndexId int NOT NULL,
        CapturedAt datetimeoffset(3) NOT NULL,
        TableName nvarchar(512) NOT NULL,
        IndexName nvarchar(512) NULL,
        IndexType nvarchar(60) NULL,
        KeyColumns nvarchar(max) NULL,
        IncludeColumns nvarchar(max) NULL,
        SizeMb decimal(19,2) NULL,
        UserSeeks bigint NULL,
        UserScans bigint NULL,
        UserLookups bigint NULL,
        UserUpdates bigint NULL,
        LastUserSeek datetimeoffset(3) NULL,
        LastUserScan datetimeoffset(3) NULL,
        LastUserLookup datetimeoffset(3) NULL,
        LastUserUpdate datetimeoffset(3) NULL,
        IsUnique bit NOT NULL,
        IsPrimaryKey bit NOT NULL,
        IsDisabled bit NOT NULL,
        HasFilter bit NOT NULL,
        FilterDefinition nvarchar(max) NULL,
        CONSTRAINT FK_SNP_Index_Server FOREIGN KEY(ServerProfileId) REFERENCES ADM.Server(Id)
    );
    CREATE INDEX IX_SNP_Index_ObjectCaptured ON SNP.[Index](ServerProfileId, DatabaseName, ObjectId, CapturedAt DESC);
END
GO

IF OBJECT_ID(N'SNP.Statistics', N'U') IS NULL
BEGIN
    CREATE TABLE SNP.Statistics
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SNP_Statistics PRIMARY KEY,
        ServerProfileId uniqueidentifier NOT NULL,
        DatabaseName sysname NOT NULL,
        ObjectId int NOT NULL,
        StatisticsId int NOT NULL,
        CapturedAt datetimeoffset(3) NOT NULL,
        TableName nvarchar(512) NOT NULL,
        StatisticsName nvarchar(512) NOT NULL,
        [Rows] bigint NULL,
        RowsSampled bigint NULL,
        ModificationCounter bigint NULL,
        LastUpdated datetimeoffset(3) NULL,
        SamplePercent decimal(9,4) NULL,
        CONSTRAINT FK_SNP_Statistics_Server FOREIGN KEY(ServerProfileId) REFERENCES ADM.Server(Id)
    );
    CREATE INDEX IX_SNP_Statistics_ObjectCaptured ON SNP.Statistics(ServerProfileId, DatabaseName, ObjectId, CapturedAt DESC);
END
GO

IF OBJECT_ID(N'SNP.Wait', N'U') IS NULL
BEGIN
    CREATE TABLE SNP.Wait
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SNP_Wait PRIMARY KEY,
        ServerProfileId uniqueidentifier NOT NULL,
        CapturedAt datetimeoffset(3) NOT NULL,
        WaitType nvarchar(120) NOT NULL,
        WaitingTasks bigint NOT NULL,
        WaitTimeMs bigint NOT NULL,
        SignalWaitMs bigint NOT NULL,
        DeltaWaitTimeMs bigint NULL,
        DeltaSignalWaitMs bigint NULL,
        CONSTRAINT FK_SNP_Wait_Server FOREIGN KEY(ServerProfileId) REFERENCES ADM.Server(Id)
    );
    CREATE INDEX IX_SNP_Wait_ServerCaptured ON SNP.Wait(ServerProfileId, CapturedAt DESC, WaitType);
END
GO

IF OBJECT_ID(N'EVT.Blocking', N'U') IS NULL
BEGIN
    CREATE TABLE EVT.Blocking
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_EVT_Blocking PRIMARY KEY,
        ServerProfileId uniqueidentifier NOT NULL,
        CapturedAt datetimeoffset(3) NOT NULL,
        SessionId smallint NOT NULL,
        BlockingSessionId smallint NULL,
        DatabaseName sysname NULL,
        WaitType nvarchar(120) NULL,
        WaitTimeMs bigint NULL,
        WaitResource nvarchar(512) NULL,
        SqlText nvarchar(max) NULL,
        ObjectName nvarchar(512) NULL,
        HostName nvarchar(255) NULL,
        ProgramName nvarchar(255) NULL,
        LoginName nvarchar(255) NULL,
        TransactionAgeMs bigint NULL,
        CONSTRAINT FK_EVT_Blocking_Server FOREIGN KEY(ServerProfileId) REFERENCES ADM.Server(Id)
    );
    CREATE INDEX IX_EVT_Blocking_ServerCaptured ON EVT.Blocking(ServerProfileId, CapturedAt DESC);
END
GO

IF OBJECT_ID(N'EVT.Deadlock', N'U') IS NULL
BEGIN
    CREATE TABLE EVT.Deadlock
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_EVT_Deadlock PRIMARY KEY,
        ServerProfileId uniqueidentifier NOT NULL,
        EventTime datetimeoffset(3) NOT NULL,
        VictimSession nvarchar(100) NULL,
        DatabaseName sysname NULL,
        ObjectNames nvarchar(max) NULL,
        DeadlockXml varbinary(max) NOT NULL,
        IsCompressed bit NOT NULL CONSTRAINT DF_EVT_Deadlock_Compressed DEFAULT(1),
        Fingerprint binary(32) NOT NULL,
        CONSTRAINT FK_EVT_Deadlock_Server FOREIGN KEY(ServerProfileId) REFERENCES ADM.Server(Id)
    );
    CREATE INDEX IX_EVT_Deadlock_ServerTime ON EVT.Deadlock(ServerProfileId, EventTime DESC);
END
GO

IF OBJECT_ID(N'ANL.Finding', N'U') IS NULL
BEGIN
    CREATE TABLE ANL.Finding
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ANL_Finding PRIMARY KEY,
        ServerProfileId uniqueidentifier NOT NULL,
        RuleId nvarchar(50) NOT NULL,
        Category nvarchar(50) NOT NULL,
        Severity int NOT NULL,
        Title nvarchar(500) NOT NULL,
        TechnicalDescription nvarchar(4000) NOT NULL,
        ConfidenceScore decimal(5,2) NOT NULL,
        ImpactScore decimal(5,2) NOT NULL,
        FindingScore decimal(7,2) NOT NULL,
        Fingerprint nvarchar(300) NOT NULL,
        FirstDetectedAt datetimeoffset(3) NOT NULL,
        LastDetectedAt datetimeoffset(3) NOT NULL,
        OccurrenceCount int NOT NULL CONSTRAINT DF_ANL_Finding_Occurrence DEFAULT(1),
        Status nvarchar(30) NOT NULL CONSTRAINT DF_ANL_Finding_Status DEFAULT(N'Open'),
        CONSTRAINT FK_ANL_Finding_Server FOREIGN KEY(ServerProfileId) REFERENCES ADM.Server(Id)
    );
    CREATE INDEX IX_ANL_Finding_OpenPriority ON ANL.Finding(ServerProfileId, Status, Severity DESC, LastDetectedAt DESC)
        INCLUDE(RuleId, Title, ConfidenceScore, ImpactScore);
    CREATE INDEX IX_ANL_Finding_Fingerprint ON ANL.Finding(ServerProfileId, Fingerprint, Status);
END
GO

IF OBJECT_ID(N'ANL.FindingEvidence', N'U') IS NULL
BEGIN
    CREATE TABLE ANL.FindingEvidence
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ANL_FindingEvidence PRIMARY KEY,
        FindingId bigint NOT NULL,
        Metric nvarchar(200) NOT NULL,
        ObservedValue nvarchar(1000) NOT NULL,
        ExpectedValue nvarchar(1000) NULL,
        Unit nvarchar(50) NULL,
        Source nvarchar(100) NOT NULL,
        Description nvarchar(2000) NULL,
        CONSTRAINT FK_ANL_FindingEvidence_Finding FOREIGN KEY(FindingId) REFERENCES ANL.Finding(Id) ON DELETE CASCADE
    );
END
GO

IF OBJECT_ID(N'REC.Recommendation', N'U') IS NULL
BEGIN
    CREATE TABLE REC.Recommendation
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_REC_Recommendation PRIMARY KEY,
        FindingId bigint NOT NULL,
        PriorityScore decimal(5,2) NOT NULL,
        Title nvarchar(500) NOT NULL,
        Explanation nvarchar(max) NOT NULL,
        ExpectedBenefit nvarchar(30) NOT NULL,
        RiskLevel nvarchar(30) NOT NULL,
        ConfidenceScore decimal(5,2) NOT NULL,
        RecommendedAction nvarchar(max) NOT NULL,
        ScriptText nvarchar(max) NULL,
        CanExecute bit NOT NULL CONSTRAINT DF_REC_Recommendation_CanExecute DEFAULT(0),
        Status nvarchar(30) NOT NULL CONSTRAINT DF_REC_Recommendation_Status DEFAULT(N'New'),
        CreatedAt datetimeoffset(3) NOT NULL CONSTRAINT DF_REC_Recommendation_CreatedAt DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT CK_REC_Recommendation_ReadOnly CHECK (CanExecute = 0),
        CONSTRAINT FK_REC_Recommendation_Finding FOREIGN KEY(FindingId) REFERENCES ANL.Finding(Id)
    );
    CREATE INDEX IX_REC_Recommendation_StatusPriority ON REC.Recommendation(Status, PriorityScore DESC);
END
GO

IF OBJECT_ID(N'BLN.ServerBaseline', N'U') IS NULL
BEGIN
    CREATE TABLE BLN.ServerBaseline
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BLN_ServerBaseline PRIMARY KEY,
        ServerProfileId uniqueidentifier NOT NULL,
        MetricName nvarchar(150) NOT NULL,
        WindowStart datetimeoffset(3) NOT NULL,
        WindowEnd datetimeoffset(3) NOT NULL,
        SampleCount int NOT NULL,
        MeanValue decimal(28,6) NULL,
        MedianValue decimal(28,6) NULL,
        P95Value decimal(28,6) NULL,
        StdDevValue decimal(28,6) NULL,
        CONSTRAINT FK_BLN_ServerBaseline_Server FOREIGN KEY(ServerProfileId) REFERENCES ADM.Server(Id)
    );
    CREATE INDEX IX_BLN_ServerBaseline_Key ON BLN.ServerBaseline(ServerProfileId, MetricName, WindowEnd DESC);
END
GO

PRINT N'SQLAdvisor database schema ready.';
GO
