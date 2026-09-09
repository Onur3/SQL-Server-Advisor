/* SQL Server Advisor - deadlock advisor schema alignment
   SQL Server 2019+ compatible and idempotent.
   Legacy EVT.Deadlock is preserved. */

USE [SQLAdvisor];
GO

IF OBJECT_ID(N'EVT.DeadlockEvent', N'U') IS NULL
BEGIN
    CREATE TABLE EVT.DeadlockEvent
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_EVT_DeadlockEvent PRIMARY KEY,
        ServerProfileId uniqueidentifier NOT NULL,
        EventTime datetimeoffset(3) NOT NULL,
        Fingerprint nvarchar(128) NOT NULL,
        VictimProcessId nvarchar(128) NULL,
        DatabaseNames nvarchar(1000) NULL,
        ObjectNames nvarchar(2000) NULL,
        DeadlockXml nvarchar(max) NOT NULL,
        CapturedAt datetimeoffset(3) NOT NULL,
        CONSTRAINT FK_EVT_DeadlockEvent_Server FOREIGN KEY(ServerProfileId) REFERENCES ADM.Server(Id)
    );

    CREATE UNIQUE INDEX UX_EVT_DeadlockEvent_Event
        ON EVT.DeadlockEvent(ServerProfileId, EventTime, Fingerprint);

    CREATE INDEX IX_EVT_DeadlockEvent_ServerTime
        ON EVT.DeadlockEvent(ServerProfileId, EventTime DESC)
        INCLUDE(Fingerprint, VictimProcessId);

    CREATE INDEX IX_EVT_DeadlockEvent_Fingerprint
        ON EVT.DeadlockEvent(ServerProfileId, Fingerprint, EventTime DESC);
END
GO

PRINT N'Deadlock advisor schema alignment ready.';
GO
