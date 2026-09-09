# SQL Server Advisor

SQL Server Advisor is a 24/7, read-only monitoring and advisory platform for Microsoft SQL Server.

## Technology

- Angular 22 frontend
- ASP.NET Core / .NET 10 Web API
- .NET 10 Windows Worker Service
- SQL Server 2025 application database
- EF Core 10, Dapper and Microsoft.Data.SqlClient
- Microsoft ScriptDom for T-SQL analysis

## Repository layout

- `backend/` — .NET solution
- `frontend/` — Angular application
- `database/` — application database and monitored-server permission templates
- `deploy/` — Windows Server / IIS deployment scripts and settings

## Centralized deployment configuration

All installation-specific values live in one file:

```text
deploy/install.settings.json
```

The scripts do not contain organization-specific domains, certificate names, server names or installation paths.

Important settings include:

```json
{
  "installation": {
    "installRoot": "C:\\Program Files\\SqlServerAdvisor",
    "siteName": "SQLServerAdvisor",
    "appPoolName": "SQLServerAdvisor",
    "workerServiceName": "SQLServerAdvisorWorker",
    "dataProtectionKeyPath": "C:\\ProgramData\\SqlServerAdvisor\\Keys"
  },
  "web": {
    "protocol": "http",
    "hostName": "localhost",
    "httpPort": 8088,
    "httpsPort": 443,
    "keepHttpBinding": false,
    "windowsAuthentication": false,
    "openFirewall": true
  },
  "certificate": {
    "mode": "auto",
    "thumbprint": "",
    "pfxPath": "",
    "pfxPasswordEnvironmentVariable": "SQLSERVERADVISOR_PFX_PASSWORD"
  },
  "database": {
    "sqlInstance": "localhost",
    "databaseName": "SQLAdvisor",
    "runMigrations": true,
    "grantApplicationIdentities": true,
    "connectionString": "",
    "connectionStringEnvironmentVariable": "SQLSERVERADVISOR_CONNECTION_STRING"
  }
}
```

### HTTP deployment

Set:

```json
"web": {
  "protocol": "http",
  "hostName": "localhost",
  "httpPort": 8088
}
```

### HTTPS deployment

For a public or internal DNS name, use a neutral host such as:

```json
"web": {
  "protocol": "https",
  "hostName": "advisor.example.com",
  "httpPort": 80,
  "httpsPort": 443,
  "keepHttpBinding": false
}
```

Certificate selection is controlled by `certificate.mode`:

- `auto` — searches `LocalMachine\My` for a valid exact-name or wildcard certificate with a private key.
- `thumbprint` — uses `certificate.thumbprint`.
- `pfx` — imports `certificate.pfxPath`; the PFX password is read from the environment variable named by `certificate.pfxPasswordEnvironmentVariable`.

Do not commit certificate passwords or production database passwords into the JSON file.

For a PFX deployment, for example:

```powershell
$env:SQLSERVERADVISOR_PFX_PASSWORD = 'set-this-securely-outside-source-control'
```

For a database connection string containing credentials, prefer:

```powershell
$env:SQLSERVERADVISOR_CONNECTION_STRING = 'Server=...;Database=SQLAdvisor;...'
```

The environment variable takes precedence over `database.connectionString`.

## Automated installation

Requirements for source-based installation:

- Windows Server 2019 or later
- .NET SDK 10.x
- Node.js 24 or later
- npm
- Microsoft `sqlcmd` when database migrations are enabled

Run PowerShell as Administrator:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\deploy\install.ps1
```

To use a different settings file:

```powershell
.\deploy\install.ps1 -SettingsPath 'C:\Config\sql-server-advisor.settings.json'
```

The installer:

1. Reads all deployment values from the settings JSON.
2. Installs required IIS Windows features.
3. Installs the .NET 10 Hosting Bundle when configured and missing.
4. Publishes the API and Worker.
5. Builds Angular for production.
6. Copies Angular into the API `wwwroot` directory.
7. Configures a single IIS application/site.
8. Creates HTTP or HTTPS bindings from the settings file.
9. Selects/imports the configured certificate for HTTPS.
10. Opens the configured Windows Firewall port when enabled.
11. Installs/updates the Worker Windows Service.
12. Runs application database migrations when enabled.
13. Configures Data Protection and filesystem ACLs.
14. Starts IIS and the Worker and performs a health check.

Default publish locations are also settings-driven. With the repository defaults they are:

```text
C:\Program Files\SqlServerAdvisor\Api
C:\Program Files\SqlServerAdvisor\Worker
```

## Certificate rebind / renewal

If a certificate is renewed or changed, edit only `deploy/install.settings.json` and run:

```powershell
.\deploy\configure-https.ps1
```

The HTTPS helper reads the same settings file as the installer.

## Production architecture

Angular is not deployed as a second IIS site. The Angular browser bundle is placed under the API publish directory at `wwwroot`.

A single IIS application serves:

- Angular static content
- `/api/...`
- `/hubs/...`
- `/health`

The Worker runs independently as a Windows Service.

## Monitored SQL Server safety boundary

The application is designed to remain read-only against monitored SQL Server instances. The monitored-server permission template is intentionally not executed automatically by the application installer. A DBA should review and apply the minimum required monitoring permissions separately.

Recommendation scripts may be generated for human review, but the application does not automatically execute those changes against monitored production servers.

## Development

Backend:

```powershell
cd backend
dotnet restore
dotnet build SqlServerAdvisor.slnx
dotnet run --project src/SqlServerAdvisor.Api
dotnet run --project src/SqlServerAdvisor.Worker
```

Frontend:

```powershell
cd frontend
npm install
npm start
```
