/*
  TEMPLATE - Run on EACH monitored SQL Server as an administrator.
  Replace the login/password values before use.
  The Advisor application never executes this script automatically.

  SQL Server 2022/2025 DMV permissions use VIEW SERVER PERFORMANCE STATE.
  Object/module visibility is provided with VIEW DEFINITION.
*/
USE [master];
GO

IF SUSER_ID(N'SqlAdvisorReader') IS NULL
BEGIN
    CREATE LOGIN [SqlAdvisorReader]
    WITH PASSWORD = N'CHANGE_THIS_TO_A_LONG_RANDOM_PASSWORD',
         CHECK_POLICY = ON,
         CHECK_EXPIRATION = ON;
END
GO

IF CONVERT(int, SERVERPROPERTY('ProductMajorVersion')) >= 16
    EXEC(N'GRANT VIEW SERVER PERFORMANCE STATE TO [SqlAdvisorReader];');
GRANT VIEW SERVER STATE TO [SqlAdvisorReader]; -- compatibility for older monitored versions / DMVs
GRANT VIEW ANY DATABASE TO [SqlAdvisorReader];
GO

/* Repeat for every database that should be analyzed. Example: */
-- USE [YourDatabase];
-- IF USER_ID(N'SqlAdvisorReader') IS NULL CREATE USER [SqlAdvisorReader] FOR LOGIN [SqlAdvisorReader];
-- GRANT VIEW DATABASE PERFORMANCE STATE TO [SqlAdvisorReader];
-- GRANT VIEW DATABASE STATE TO [SqlAdvisorReader];
-- GRANT VIEW DEFINITION TO [SqlAdvisorReader];
-- GRANT CONNECT TO [SqlAdvisorReader];
GO
