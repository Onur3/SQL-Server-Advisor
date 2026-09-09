/* SQL Server Advisor - deadlock advisor schema alignment
   SQL Server 2019+ compatible and idempotent.
   The incompatible legacy EVT.Deadlock table is preserved as EVT.DeadlockLegacy. */

USE [SQLAdvisor];
GO

/* Preserve the early schema instead of destructively converting binary/XML columns. */
IF OBJECT_ID(N'EVT.Deadlock', N'U') IS NOT NULL
AND COL_LENGTH(N'EVT.Deadlock', N'CapturedAt') IS NULL
AND OBJECT_ID(N'EVT.DeadlockLegacy', N'U') IS NULL
BEGIN
    EXEC sys.sp_rename N'EVT.Deadlock', N'DeadlockLegacy';
END
GO

/* Compatibility with an intermediate migration version, if it was ever applied. */
IF OBJECT_ID(N'EVT.Deadlock', N'U') IS NULL
AND OBJECT_ID(N'EVT.DeadlockEvent', N'U') IS NOT NULL
BEGIN
    EXEC sys.sp_rename N'EVT.DeadlockEvent', N'Deadlock';
END
GO

IF OBJECT_ID(N'EVT.Deadlock', N'U') IS NULL
BEGIN
    CREATE TABLE EVT.Deadlock
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_EVT_Deadlock_Current PRIMARY KEY,
        ServerProfileId uniqueidentifier NOT NULL,
        EventTime datetimeoffset(3) NOT NULL,
        Fingerprint nvarchar(128) NOT NULL,
        VictimProcessId nvarchar(128) NULL,
        DatabaseNames nvarchar(1000) NULL,
        ObjectNames nvarchar(2000) NULL,
        DeadlockXml nvarchar(max) NOT NULL,
        CapturedAt datetimeoffset(3) NOT NULL,
        CONSTRAINT FK_EVT_Deadlock_Current_Server FOREIGN KEY(ServerProfileId) REFERENCES ADM.Server(Id)
    );

    CREATE UNIQUE INDEX UX_EVT_Deadlock_Current_Event
        ON EVT.Deadlock(ServerProfileId, EventTime, Fingerprint);

    CREATE INDEX IX_EVT_Deadlock_Current_ServerTime
        ON EVT.Deadlock(ServerProfileId, EventTime DESC)
        INCLUDE(Fingerprint, VictimProcessId);

    CREATE INDEX IX_EVT_Deadlock_Current_Fingerprint
        ON EVT.Deadlock(ServerProfileId, Fingerprint, EventTime DESC);
END
GO

PRINT N'Deadlock advisor schema alignment ready.';
GO
