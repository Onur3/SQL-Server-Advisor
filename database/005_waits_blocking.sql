USE [SQLAdvisor];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
UPDATE SNP.Wait SET DeltaWaitTimeMs=0 WHERE DeltaWaitTimeMs IS NULL;
UPDATE SNP.Wait SET DeltaSignalWaitMs=0 WHERE DeltaSignalWaitMs IS NULL;
ALTER TABLE SNP.Wait ALTER COLUMN DeltaWaitTimeMs bigint NOT NULL;
ALTER TABLE SNP.Wait ALTER COLUMN DeltaSignalWaitMs bigint NOT NULL;
IF COL_LENGTH('SNP.Wait','SqlServerStartTime') IS NULL
    ALTER TABLE SNP.Wait ADD SqlServerStartTime datetime2 NULL;
IF COL_LENGTH('SNP.Wait','IsBaseline') IS NULL
    ALTER TABLE SNP.Wait ADD IsBaseline bit NOT NULL CONSTRAINT DF_Wait_Baseline DEFAULT 1;
IF COL_LENGTH('SNP.Wait','IntervalMs') IS NULL
    ALTER TABLE SNP.Wait ADD IntervalMs bigint NOT NULL CONSTRAINT DF_Wait_Interval DEFAULT 0;
UPDATE EVT.Blocking SET BlockingSessionId=0 WHERE BlockingSessionId IS NULL;
UPDATE EVT.Blocking SET WaitTimeMs=0 WHERE WaitTimeMs IS NULL;
ALTER TABLE EVT.Blocking ALTER COLUMN SessionId int NOT NULL;
ALTER TABLE EVT.Blocking ALTER COLUMN BlockingSessionId int NOT NULL;
ALTER TABLE EVT.Blocking ALTER COLUMN WaitTimeMs bigint NOT NULL;
ALTER TABLE EVT.Blocking ALTER COLUMN WaitResource nvarchar(1000) NULL;
IF COL_LENGTH('EVT.Blocking','RequestId') IS NULL
    ALTER TABLE EVT.Blocking ADD RequestId int NOT NULL CONSTRAINT DF_Blocking_Request DEFAULT 0;
IF COL_LENGTH('EVT.Blocking','BlockerStatus') IS NULL
    ALTER TABLE EVT.Blocking ADD BlockerStatus nvarchar(30) NULL;
IF COL_LENGTH('EVT.Blocking','BlockerSqlText') IS NULL
    ALTER TABLE EVT.Blocking ADD BlockerSqlText nvarchar(max) NULL;
IF COL_LENGTH('EVT.Blocking','BlockerOpenTransactions') IS NULL
    ALTER TABLE EVT.Blocking ADD BlockerOpenTransactions int NULL;
COMMIT;
GO
