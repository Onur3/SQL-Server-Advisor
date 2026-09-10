USE [SQLAdvisor];
GO

IF OBJECT_ID(N'ADM.MonitoredTableScope', N'U') IS NULL
BEGIN
    CREATE TABLE ADM.MonitoredTableScope
    (
        Id bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_ADM_MonitoredTableScope PRIMARY KEY,
        ServerProfileId uniqueidentifier NOT NULL,
        DatabaseName nvarchar(128) NOT NULL,
        SchemaName nvarchar(128) NOT NULL,
        TableName nvarchar(128) NOT NULL,
        CreatedAt datetimeoffset(7) NOT NULL
            CONSTRAINT DF_ADM_MonitoredTableScope_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ADM_MonitoredTableScope_Server
            FOREIGN KEY (ServerProfileId) REFERENCES ADM.[Server](Id) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'ADM.MonitoredTableScope')
      AND name = N'UX_ADM_MonitoredTableScope_ServerDatabaseTable'
)
BEGIN
    CREATE UNIQUE INDEX UX_ADM_MonitoredTableScope_ServerDatabaseTable
        ON ADM.MonitoredTableScope(ServerProfileId, DatabaseName, SchemaName, TableName);
END;
GO
