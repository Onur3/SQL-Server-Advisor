/* SQL Server Advisor - wait/blocking telemetry schema alignment
   SQL Server 2019+ compatible and idempotent. */

USE [SQLAdvisor];
GO

IF COL_LENGTH(N'SNP.Wait', N'SignalWaitTimeMs') IS NULL
BEGIN
    ALTER TABLE SNP.Wait
        ADD SignalWaitTimeMs bigint NOT NULL
            CONSTRAINT DF_SNP_Wait_SignalWaitTimeMs DEFAULT (0);
END
GO

IF COL_LENGTH(N'SNP.Wait', N'SignalWaitMs') IS NOT NULL
   AND COL_LENGTH(N'SNP.Wait', N'SignalWaitTimeMs') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        UPDATE SNP.Wait
        SET SignalWaitTimeMs = ISNULL(SignalWaitMs, 0);';
END
GO

IF COL_LENGTH(N'SNP.Wait', N'DeltaSignalWaitTimeMs') IS NULL
BEGIN
    ALTER TABLE SNP.Wait
        ADD DeltaSignalWaitTimeMs bigint NOT NULL
            CONSTRAINT DF_SNP_Wait_DeltaSignalWaitTimeMs DEFAULT (0);
END
GO

IF COL_LENGTH(N'SNP.Wait', N'DeltaSignalWaitMs') IS NOT NULL
   AND COL_LENGTH(N'SNP.Wait', N'DeltaSignalWaitTimeMs') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        UPDATE SNP.Wait
        SET DeltaSignalWaitTimeMs = ISNULL(DeltaSignalWaitMs, 0);';
END
GO

IF COL_LENGTH(N'SNP.Wait', N'DeltaWaitTimeMs') IS NOT NULL
BEGIN
    UPDATE SNP.Wait SET DeltaWaitTimeMs = 0 WHERE DeltaWaitTimeMs IS NULL;
    ALTER TABLE SNP.Wait ALTER COLUMN DeltaWaitTimeMs bigint NOT NULL;
END
GO

IF COL_LENGTH(N'EVT.Blocking', N'BlockingSessionId') IS NOT NULL
BEGIN
    UPDATE EVT.Blocking SET BlockingSessionId = 0 WHERE BlockingSessionId IS NULL;
    ALTER TABLE EVT.Blocking ALTER COLUMN BlockingSessionId smallint NOT NULL;
END
GO

IF COL_LENGTH(N'EVT.Blocking', N'WaitTimeMs') IS NOT NULL
BEGIN
    UPDATE EVT.Blocking SET WaitTimeMs = 0 WHERE WaitTimeMs IS NULL;
    ALTER TABLE EVT.Blocking ALTER COLUMN WaitTimeMs bigint NOT NULL;
END
GO

PRINT N'Wait and blocking telemetry schema alignment ready.';
GO
