/* SQL Server Advisor - legacy query context cleanup
   Keeps query history intact while removing raw <unknown> labels. */

USE [SQLAdvisor];
GO

IF OBJECT_ID(N'QRY.Query', N'U') IS NOT NULL
AND COL_LENGTH(N'QRY.Query', N'DatabaseName') IS NOT NULL
BEGIN
    UPDATE QRY.Query
    SET DatabaseName = N'Ad-hoc / DB bağlamı yok'
    WHERE DatabaseName = N'<unknown>' OR LTRIM(RTRIM(DatabaseName)) = N'';
END
GO

IF OBJECT_ID(N'ANL.Finding', N'U') IS NOT NULL
BEGIN
    UPDATE ANL.Finding
    SET Title = REPLACE(Title, N'<unknown>', N'Ad-hoc sorgu'),
        DatabaseName = NULL
    WHERE RuleId = N'QRY-001'
      AND (Title LIKE N'%<unknown>%' OR DatabaseName = N'<unknown>');
END
GO

PRINT N'Legacy query context labels normalized.';
GO
