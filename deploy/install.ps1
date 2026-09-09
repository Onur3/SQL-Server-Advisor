[CmdletBinding()]
param(
    [string]$SourceRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$InstallRoot = 'C:\Program Files\SqlServerAdvisor',
    [string]$SiteName = 'SQLServerAdvisor',
    [string]$AppPoolName = 'SQLServerAdvisor',
    [string]$HostName = $env:COMPUTERNAME,
    [int]$HttpPort = 8088,
    [string]$SqlInstance = 'localhost',
    [string]$WorkerServiceName = 'SQLServerAdvisorWorker',
    [string]$WorkerDisplayName = 'SQL Server Advisor Collector',
    [string]$DataProtectionKeyPath = 'C:\ProgramData\SqlServerAdvisor\Keys',
    [string]$AdvisorConnectionString = '',
    [switch]$EnableWindowsAuthentication,
    [switch]$SkipDatabase,
    [switch]$SkipIdentityGrants,
    [switch]$SkipHostingBundleInstall,
    [string]$HostingBundleUrl = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Write-Step([string]$Message) {
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Bu kurulum scripti Administrator olarak calistirilmalidir.'
    }
}

function Require-Command([string]$Name, [string]$HelpText) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name bulunamadi. $HelpText"
    }
}

function Invoke-External([string]$FilePath, [string[]]$Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath exit code $LASTEXITCODE ile basarisiz oldu."
    }
}

function Test-DotNet10Sdk {
    $versionText = (& dotnet --version 2>$null)
    if (-not $versionText) { return $false }
    try { return ([version]($versionText.Split('-')[0])).Major -eq 10 } catch { return $false }
}

function Test-Node24 {
    $versionText = (& node --version 2>$null)
    if (-not $versionText) { return $false }
    try { return ([version]($versionText.TrimStart('v').Split('-')[0])).Major -ge 24 } catch { return $false }
}

function Ensure-Iis {
    Write-Step 'IIS Windows Server rolu kontrol ediliyor'
    if (-not (Get-Module -ListAvailable ServerManager)) {
        throw 'ServerManager PowerShell modulu bulunamadi. Bu script Windows Server 2019+ icin tasarlanmistir.'
    }

    Import-Module ServerManager
    $features = @(
        'Web-Server',
        'Web-Static-Content',
        'Web-Default-Doc',
        'Web-Http-Errors',
        'Web-Http-Logging',
        'Web-Request-Monitor',
        'Web-Filtering',
        'Web-Mgmt-Console'
    )
    if ($EnableWindowsAuthentication) {
        $features += 'Web-Windows-Auth'
    }

    $missing = @()
    foreach ($feature in $features) {
        $state = Get-WindowsFeature -Name $feature
        if (-not $state.Installed) { $missing += $feature }
    }

    if ($missing.Count -gt 0) {
        Write-Host "IIS ozellikleri kuruluyor: $($missing -join ', ')"
        $result = Install-WindowsFeature -Name $missing -IncludeManagementTools
        if (-not $result.Success) { throw 'IIS Windows Features kurulumu basarisiz oldu.' }
        if ($result.RestartNeeded -eq 'Yes') {
            Write-Warning 'Windows Feature kurulumu restart isteyebilir. Kurulum devam edecek; gerekirse islem sonunda sunucuyu yeniden baslatin.'
        }
    }
}

function Test-AspNetCoreModule {
    return (Test-Path "$env:ProgramFiles\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll") -or
           (Test-Path "${env:ProgramFiles(x86)}\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll")
}

function Ensure-HostingBundle {
    if (Test-AspNetCoreModule) {
        Write-Host 'ASP.NET Core Module V2 mevcut.'
        return
    }

    if ($SkipHostingBundleInstall) {
        throw 'ASP.NET Core Hosting Bundle bulunamadi ve -SkipHostingBundleInstall secildi.'
    }

    Write-Step '.NET 10 Hosting Bundle kuruluyor'
    $downloadUrl = $HostingBundleUrl

    if ([string]::IsNullOrWhiteSpace($downloadUrl)) {
        $permalink = 'https://dotnet.microsoft.com/permalink/dotnetcore-current-windows-runtime-bundle-installer'
        $page = Invoke-WebRequest -Uri $permalink -UseBasicParsing
        $match = [regex]::Match($page.Content, 'https://builds\.dotnet\.microsoft\.com/[^"''<> ]+/dotnet-hosting-10\.[^"''<> ]+-win\.exe')
        if (-not $match.Success) {
            throw 'Microsoft sayfasindan .NET 10 Hosting Bundle indirme adresi otomatik bulunamadi. -HostingBundleUrl ile dogrudan URL verin.'
        }
        $downloadUrl = $match.Value
    }

    if ($downloadUrl -notmatch 'dotnet-hosting-10\.') {
        throw 'HostingBundleUrl .NET 10 Hosting Bundle adresi olmali.'
    }

    $installer = Join-Path $env:TEMP 'dotnet-hosting-10-win.exe'
    Invoke-WebRequest -Uri $downloadUrl -OutFile $installer -UseBasicParsing
    $process = Start-Process -FilePath $installer -ArgumentList '/install','/quiet','/norestart' -Wait -PassThru
    if ($process.ExitCode -ne 0 -and $process.ExitCode -ne 3010) {
        throw ".NET Hosting Bundle kurulumu exit code $($process.ExitCode) ile basarisiz oldu."
    }

    if (-not (Test-AspNetCoreModule)) {
        throw 'Hosting Bundle kuruldu ancak ASP.NET Core Module V2 bulunamadi.'
    }
}

function Invoke-SqlFile([string]$Path) {
    Invoke-External 'sqlcmd.exe' @('-S', $SqlInstance, '-E', '-C', '-b', '-i', $Path)
}

function Invoke-SqlQuery([string]$Query) {
    Invoke-External 'sqlcmd.exe' @('-S', $SqlInstance, '-E', '-C', '-b', '-Q', $Query)
}

function Escape-SqlIdentifier([string]$Value) {
    return $Value.Replace(']', ']]')
}

function Escape-SqlString([string]$Value) {
    return $Value.Replace("'", "''")
}

function Configure-Iis([string]$ApiPath) {
    Write-Step 'IIS site ve Application Pool yapilandiriliyor'
    Import-Module WebAdministration

    $appPoolPath = "IIS:\AppPools\$AppPoolName"
    if (-not (Test-Path $appPoolPath)) {
        New-WebAppPool -Name $AppPoolName | Out-Null
    }

    Set-ItemProperty $appPoolPath -Name managedRuntimeVersion -Value ''
    Set-ItemProperty $appPoolPath -Name enable32BitAppOnWin64 -Value $false
    Set-ItemProperty $appPoolPath -Name processModel.identityType -Value 4
    Set-ItemProperty $appPoolPath -Name startMode -Value 'AlwaysRunning'

    $existingSite = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
    if (-not $existingSite) {
        New-Website -Name $SiteName -PhysicalPath $ApiPath -ApplicationPool $AppPoolName -Port $HttpPort -HostHeader $HostName | Out-Null
    }
    else {
        Stop-Website -Name $SiteName -ErrorAction SilentlyContinue
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $ApiPath
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName

        Get-WebBinding -Name $SiteName -Protocol 'http' -ErrorAction SilentlyContinue | ForEach-Object {
            Remove-WebBinding -Name $SiteName -Protocol 'http' -BindingInformation $_.bindingInformation
        }
        New-WebBinding -Name $SiteName -Protocol 'http' -Port $HttpPort -HostHeader $HostName | Out-Null
    }

    if ($EnableWindowsAuthentication) {
        Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $SiteName -Filter 'system.webServer/security/authentication/windowsAuthentication' -Name enabled -Value $true
        Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $SiteName -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name enabled -Value $false
    }

    Start-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    Start-Website -Name $SiteName
}

function Configure-WorkerService([string]$WorkerPath) {
    Write-Step 'Worker Windows Service yapilandiriliyor'
    $workerExe = Join-Path $WorkerPath 'SqlServerAdvisor.Worker.exe'
    if (-not (Test-Path $workerExe)) { throw "Worker executable bulunamadi: $workerExe" }

    $existing = Get-Service -Name $WorkerServiceName -ErrorAction SilentlyContinue
    if ($existing) {
        Stop-Service -Name $WorkerServiceName -Force -ErrorAction SilentlyContinue
        Invoke-External 'sc.exe' @('config', $WorkerServiceName, 'binPath=', "`"$workerExe`"", 'start=', 'auto', 'obj=', "NT SERVICE\$WorkerServiceName")
    }
    else {
        Invoke-External 'sc.exe' @('create', $WorkerServiceName, 'binPath=', "`"$workerExe`"", 'DisplayName=', $WorkerDisplayName, 'start=', 'auto', 'obj=', "NT SERVICE\$WorkerServiceName")
    }

    Invoke-External 'sc.exe' @('failure', $WorkerServiceName, 'reset=', '86400', 'actions=', 'restart/5000/restart/10000/restart/30000')
    Invoke-External 'sc.exe' @('sidtype', $WorkerServiceName, 'unrestricted')
}

function Grant-ApplicationDatabasePermissions {
    if ($SkipIdentityGrants) {
        Write-Host 'SQL identity grant adimi atlandi.'
        return
    }

    Write-Step 'IIS ve Worker servis kimliklerine SQLAdvisor izinleri veriliyor'
    $apiPrincipal = "IIS APPPOOL\$AppPoolName"
    $workerPrincipal = "NT SERVICE\$WorkerServiceName"

    $apiId = Escape-SqlIdentifier $apiPrincipal
    $workerId = Escape-SqlIdentifier $workerPrincipal
    $apiString = Escape-SqlString $apiPrincipal
    $workerString = Escape-SqlString $workerPrincipal

    $sql = @"
USE [master];
IF SUSER_ID(N'$apiString') IS NULL CREATE LOGIN [$apiId] FROM WINDOWS;
IF SUSER_ID(N'$workerString') IS NULL CREATE LOGIN [$workerId] FROM WINDOWS;
USE [SQLAdvisor];
IF USER_ID(N'$apiString') IS NULL CREATE USER [$apiId] FOR LOGIN [$apiId];
IF USER_ID(N'$workerString') IS NULL CREATE USER [$workerId] FOR LOGIN [$workerId];
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members drm JOIN sys.database_principals r ON r.principal_id=drm.role_principal_id JOIN sys.database_principals m ON m.principal_id=drm.member_principal_id WHERE r.name=N'db_datareader' AND m.name=N'$apiString') ALTER ROLE [db_datareader] ADD MEMBER [$apiId];
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members drm JOIN sys.database_principals r ON r.principal_id=drm.role_principal_id JOIN sys.database_principals m ON m.principal_id=drm.member_principal_id WHERE r.name=N'db_datawriter' AND m.name=N'$apiString') ALTER ROLE [db_datawriter] ADD MEMBER [$apiId];
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members drm JOIN sys.database_principals r ON r.principal_id=drm.role_principal_id JOIN sys.database_principals m ON m.principal_id=drm.member_principal_id WHERE r.name=N'db_datareader' AND m.name=N'$workerString') ALTER ROLE [db_datareader] ADD MEMBER [$workerId];
IF NOT EXISTS (SELECT 1 FROM sys.database_role_members drm JOIN sys.database_principals r ON r.principal_id=drm.role_principal_id JOIN sys.database_principals m ON m.principal_id=drm.member_principal_id WHERE r.name=N'db_datawriter' AND m.name=N'$workerString') ALTER ROLE [db_datawriter] ADD MEMBER [$workerId];
GRANT EXECUTE TO [$apiId];
GRANT EXECUTE TO [$workerId];
"@
    Invoke-SqlQuery $sql
}

function Grant-FilePermissions([string]$ApiPath, [string]$WorkerPath) {
    Write-Step 'Dosya sistemi ACL izinleri ayarlaniyor'
    $apiPrincipal = "IIS APPPOOL\$AppPoolName"
    $workerPrincipal = "NT SERVICE\$WorkerServiceName"

    New-Item -ItemType Directory -Force -Path $DataProtectionKeyPath | Out-Null
    Invoke-External 'icacls.exe' @($DataProtectionKeyPath, '/grant', "${apiPrincipal}:(OI)(CI)M", "${workerPrincipal}:(OI)(CI)M", '/T', '/C')
    Invoke-External 'icacls.exe' @($ApiPath, '/grant', "${apiPrincipal}:(OI)(CI)RX", '/T', '/C')
    Invoke-External 'icacls.exe' @($WorkerPath, '/grant', "${workerPrincipal}:(OI)(CI)RX", '/T', '/C')
}

function Write-ProductionSettings([string]$ApiPath, [string]$WorkerPath) {
    $connectionString = $AdvisorConnectionString
    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        $connectionString = "Server=$SqlInstance;Database=SQLAdvisor;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Application Name=SQLServerAdvisor"
    }

    $origin = "http://${HostName}:$HttpPort"
    if ($HttpPort -eq 80) { $origin = "http://$HostName" }

    $apiSettings = [ordered]@{
        ConnectionStrings = @{ AdvisorDatabase = $connectionString }
        Security = @{ DataProtectionKeyPath = $DataProtectionKeyPath }
        HttpsRedirection = @{ Enabled = $false }
        Cors = @{ Origins = @($origin) }
        Logging = @{ LogLevel = @{ Default = 'Information'; 'Microsoft.AspNetCore' = 'Warning' } }
        AllowedHosts = '*'
    }
    $workerSettings = [ordered]@{
        ConnectionStrings = @{ AdvisorDatabase = $connectionString }
        Security = @{ DataProtectionKeyPath = $DataProtectionKeyPath }
        Logging = @{ LogLevel = @{ Default = 'Information'; 'Microsoft.Hosting.Lifetime' = 'Information' } }
    }

    $apiSettings | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $ApiPath 'appsettings.Production.json') -Encoding UTF8
    $workerSettings | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $WorkerPath 'appsettings.Production.json') -Encoding UTF8
}

Assert-Administrator

$SourceRoot = [IO.Path]::GetFullPath($SourceRoot)
$InstallRoot = [IO.Path]::GetFullPath($InstallRoot)
$apiProject = Join-Path $SourceRoot 'backend\src\SqlServerAdvisor.Api\SqlServerAdvisor.Api.csproj'
$workerProject = Join-Path $SourceRoot 'backend\src\SqlServerAdvisor.Worker\SqlServerAdvisor.Worker.csproj'
$frontendRoot = Join-Path $SourceRoot 'frontend'
$databaseRoot = Join-Path $SourceRoot 'database'

foreach ($requiredPath in @($apiProject, $workerProject, (Join-Path $frontendRoot 'package.json'), $databaseRoot)) {
    if (-not (Test-Path $requiredPath)) { throw "Gerekli kaynak bulunamadi: $requiredPath" }
}

Write-Step 'Build araclari kontrol ediliyor'
Require-Command 'dotnet.exe' '.NET 10 SDK kurulu olmali.'
Require-Command 'node.exe' 'Node.js 24 LTS kurulu olmali.'
Require-Command 'npm.cmd' 'npm kurulu olmali.'
if (-not (Test-DotNet10Sdk)) { throw '.NET SDK major version 10 bulunamadi.' }
if (-not (Test-Node24)) { throw 'Node.js 24 veya daha yeni bir surum gerekli.' }

Ensure-Iis
Ensure-HostingBundle

if (-not $SkipDatabase) {
    Require-Command 'sqlcmd.exe' 'Microsoft sqlcmd kurulu olmali ve PATH icinde bulunmali.'
}

$stageRoot = Join-Path $env:TEMP ("SqlServerAdvisor-Install-" + [guid]::NewGuid().ToString('N'))
$apiStage = Join-Path $stageRoot 'Api'
$workerStage = Join-Path $stageRoot 'Worker'
$apiPath = Join-Path $InstallRoot 'Api'
$workerPath = Join-Path $InstallRoot 'Worker'

try {
    New-Item -ItemType Directory -Force -Path $apiStage, $workerStage | Out-Null

    Write-Step '.NET API Release publish aliniyor'
    Invoke-External 'dotnet.exe' @('publish', $apiProject, '-c', 'Release', '-o', $apiStage)

    Write-Step '.NET Worker Release publish aliniyor'
    Invoke-External 'dotnet.exe' @('publish', $workerProject, '-c', 'Release', '-o', $workerStage)

    Write-Step 'Angular production build aliniyor'
    Push-Location $frontendRoot
    try {
        if (Test-Path (Join-Path $frontendRoot 'package-lock.json')) {
            Invoke-External 'npm.cmd' @('ci', '--no-audit', '--no-fund')
        }
        else {
            Invoke-External 'npm.cmd' @('install', '--no-audit', '--no-fund')
        }
        Invoke-External 'npm.cmd' @('run', 'build')
    }
    finally {
        Pop-Location
    }

    $browserPath = Join-Path $frontendRoot 'dist\sql-server-advisor-ui\browser'
    if (-not (Test-Path (Join-Path $browserPath 'index.html'))) {
        throw "Angular browser output bulunamadi: $browserPath"
    }

    $wwwroot = Join-Path $apiStage 'wwwroot'
    New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
    Copy-Item -Path (Join-Path $browserPath '*') -Destination $wwwroot -Recurse -Force

    Write-Step 'Mevcut servis/site durduruluyor ve dosyalar yerlestiriliyor'
    $service = Get-Service -Name $WorkerServiceName -ErrorAction SilentlyContinue
    if ($service) { Stop-Service -Name $WorkerServiceName -Force -ErrorAction SilentlyContinue }

    if (Get-Module -ListAvailable WebAdministration) {
        Import-Module WebAdministration
        if (Get-Website -Name $SiteName -ErrorAction SilentlyContinue) {
            Stop-Website -Name $SiteName -ErrorAction SilentlyContinue
        }
    }

    New-Item -ItemType Directory -Force -Path $apiPath, $workerPath | Out-Null
    Remove-Item -Path (Join-Path $apiPath '*') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $workerPath '*') -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item -Path (Join-Path $apiStage '*') -Destination $apiPath -Recurse -Force
    Copy-Item -Path (Join-Path $workerStage '*') -Destination $workerPath -Recurse -Force

    Write-ProductionSettings -ApiPath $apiPath -WorkerPath $workerPath
    Configure-Iis -ApiPath $apiPath
    Configure-WorkerService -WorkerPath $workerPath

    if (-not $SkipDatabase) {
        Write-Step 'SQLAdvisor veritabani migration scriptleri calistiriliyor'
        $migrationFiles = Get-ChildItem -Path $databaseRoot -Filter '*.sql' -File |
            Where-Object { $_.Name -notmatch 'template' } |
            Sort-Object Name

        if ($migrationFiles.Count -eq 0) { throw 'Calistirilacak database migration scripti bulunamadi.' }
        foreach ($migration in $migrationFiles) {
            Write-Host "  -> $($migration.Name)"
            Invoke-SqlFile $migration.FullName
        }

        Grant-ApplicationDatabasePermissions
    }

    Grant-FilePermissions -ApiPath $apiPath -WorkerPath $workerPath

    Write-Step 'Servisler baslatiliyor'
    Start-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    Start-Website -Name $SiteName -ErrorAction SilentlyContinue
    Start-Service -Name $WorkerServiceName

    Write-Step 'Health check yapiliyor'
    $healthOk = $false
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            $headers = @{ Host = $HostName }
            $response = Invoke-WebRequest -Uri "http://127.0.0.1:$HttpPort/health" -Headers $headers -UseBasicParsing -UseDefaultCredentials -TimeoutSec 10
            if ($response.StatusCode -eq 200) { $healthOk = $true; break }
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }
    if (-not $healthOk) {
        throw 'Kurulum tamamlandi ancak /health endpoint kontrolu basarisiz oldu. IIS Event Viewer ve application loglarini kontrol edin.'
    }

    Write-Host "`nSQL Server Advisor kurulumu basariyla tamamlandi." -ForegroundColor Green
    Write-Host "Site       : http://${HostName}:$HttpPort"
    Write-Host "IIS Site   : $SiteName"
    Write-Host "App Pool   : $AppPoolName"
    Write-Host "Worker     : $WorkerServiceName"
    Write-Host "Install    : $InstallRoot"
    Write-Host "Database   : SQLAdvisor @ $SqlInstance"
}
finally {
    if (Test-Path $stageRoot) {
        Remove-Item -Path $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
