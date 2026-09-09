/*
  SQLAdvisor application database permissions template.
  Preferred production model: run API and Worker under dedicated Windows service accounts.
  Replace DOMAIN\... values with your actual accounts.
*/
USE [master];
GO

-- Example:
-- CREATE LOGIN [DOMAIN\SqlAdvisorApi] FROM WINDOWS;
-- CREATE LOGIN [DOMAIN\SqlAdvisorWorker] FROM WINDOWS;
GO

USE [SQLAdvisor];
GO

-- Example:
-- CREATE USER [DOMAIN\SqlAdvisorApi] FOR LOGIN [DOMAIN\SqlAdvisorApi];
-- CREATE USER [DOMAIN\SqlAdvisorWorker] FOR LOGIN [DOMAIN\SqlAdvisorWorker];
-- ALTER ROLE db_datareader ADD MEMBER [DOMAIN\SqlAdvisorApi];
-- ALTER ROLE db_datawriter ADD MEMBER [DOMAIN\SqlAdvisorApi];
-- ALTER ROLE db_datareader ADD MEMBER [DOMAIN\SqlAdvisorWorker];
-- ALTER ROLE db_datawriter ADD MEMBER [DOMAIN\SqlAdvisorWorker];
GO
