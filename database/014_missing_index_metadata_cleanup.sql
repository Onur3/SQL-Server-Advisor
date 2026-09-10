/* SQL Server Advisor - missing-index metadata cleanup
   SQL Server 2019+ compatible and idempotent.

   Older collector versions could persist an empty TableName when
   OBJECT_NAME / OBJECT_SCHEMA_NAME returned NULL because metadata was not
   visible to the monitored login. Those rows can generate malformed draft
   SQL such as: CREATE INDEX ... ON  ([Column]).
*/

USE [SQLAdvisor];
GO

IF OBJECT_ID(N'SNP.MissingIndex', N'U') IS NOT NULL
BEGIN
    DELETE FROM SNP.MissingIndex
    WHERE NULLIF(LTRIM(RTRIM(TableName)), N'') IS NULL;
END;
GO

PRINT N'Invalid missing-index metadata snapshots cleaned.';
GO
