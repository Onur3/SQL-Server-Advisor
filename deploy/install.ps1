[CmdletBinding()]
param(
    [string]$SettingsPath = (Join-Path $PSScriptRoot 'install.settings.json')
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
        throw 'This installer must be run from an elevated PowerShell session.'
    }
}

function Require-Command([string]$Name, [string]$HelpText) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name was not found. $HelpText"
    }
}

function Invoke-External([string]$FilePath, [string[]]$Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function Get-RequiredProperty($Object, [string]$PropertyName, [string]$SectionName) {
    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        throw "Missing setting '$SectionName.$PropertyName'."
    }
    return $property.Value
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

function Test-AspNetCoreModule {
    return (Test-Path "$env:ProgramFiles\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll") -or
           (Test-Path "${env:ProgramFiles(x86)}\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll")
}

function Ensure-Iis {
    Write-Step 'Checking IIS Windows Server features'
    if (-not (Get-Module -ListAvailable ServerManager)) {
        throw 'ServerManager PowerShell module was not found. Windows Server 2019 or later is required.'
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

    if ($WindowsAuthentication) {
        $features += 'Web-Windows-Auth'
    }

    $missing = @()
    foreach ($feature in $features) {
        $state = Get-WindowsFeature -Name $feature
        if (-not $state.Installed) { $missing += $feature }
    }

    if ($missing.Count -gt 0) {
        $result = Install-WindowsFeature -Name $missing -IncludeManagementTools
        if (-not $result.Success) { throw 'IIS Windows Feature installation failed.' }
        if ($result.RestartNeeded -eq 'Yes') {
            Write-Warning 'Windows reports that a restart may be required after feature installation.'
        }
    }
}

function Ensure-HostingBundle {
    if (Test-AspNetCoreModule) { return }
    if (-not $InstallHostingBundleIfMissing) {
        throw 'ASP.NET Core Module V2 is missing and build.installHostingBundleIfMissing is false.'
    }

    Write-Step 'Installing .NET 10 Hosting Bundle'
    $downloadUrl = $HostingBundleUrl
    if ([string]::IsNullOrWhiteSpace($downloadUrl)) {
        $permalink = 'https://dotnet.microsoft.com/permalink/dotnetcore-current-windows-runtime-bundle-installer'
        $page = Invoke-WebRequest -Uri $permalink -UseBasicParsing
        $match = [regex]::Match($page.Content, 'https://builds\.dotnet\.microsoft\.com/[^"''<> ]+/dotnet-hosting-10\.[^"''<> ]+-win\.exe')
        if (-not $match.Success) {
            throw 'Could not resolve the .NET 10 Hosting Bundle URL. Set build.hostingBundleUrl explicitly.'
        }
        $downloadUrl = $match.Value
    }

    if ($downloadUrl -notmatch 'dotnet-hosting-10\.') {
        throw 'build.hostingBundleUrl must point to a .NET 10 Hosting Bundle installer.'
    }

    $installer = Join-Path $env:TEMP 'dotnet-hosting-10-win.exe'
    Invoke-WebRequest -Uri $downloadUrl -OutFile $installer -UseBasicParsing
    $process = Start-Process -FilePath $installer -ArgumentList '/install','/quiet','/norestart' -Wait -PassThru
    if ($process.ExitCode -ne 0 -and $process.ExitCode -ne 3010) {
        throw ".NET Hosting Bundle installation failed with exit code $($process.ExitCode)."
    }

    if (-not (Test-AspNetCoreModule)) {
        throw 'Hosting Bundle completed but ASP.NET Core Module V2 is still unavailable.'
    }
}

function Escape-SqlIdentifier([string]$Value) { return $Value.Replace(']', ']]') }
function Escape-SqlString([string]$Value) { return $Value.Replace("'", "''") }

function Invoke-SqlFile([string]$Path) {
    Invoke-External 'sqlcmd.exe' @('-S', $SqlInstance, '-E', '-C', '-b', '-i', $Path)
}

function Invoke-SqlQuery([string]$Query) {
    Invoke-External 'sqlcmd.exe' @('-S', $SqlInstance, '-E', '-C', '-b', '-Q', $Query)
}

function Get-CertificateDnsNames([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate) {
    $names = @()
    try { $names += @($Certificate.DnsNameList | ForEach-Object { $_.Unicode }) } catch { }
    if ($names.Count -eq 0 -and $Certificate.Subject -match '(?:^|,\s*)CN=([^,]+)') {
        $names += $Matches[1].Trim()
    }
    return @($names | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
}

function Test-DnsNameMatch([string]$Pattern, [string]$DnsName) {
    if ([string]::Equals($Pattern, $DnsName, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ($Pattern.StartsWith('*.')) {
        $suffix = $Pattern.Substring(1)
        if (-not $DnsName.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)) { return $false }
        $left = $DnsName.Substring(0, $DnsName.Length - $suffix.Length)
        return (-not [string]::IsNullOrWhiteSpace($left)) -and (-not $left.Contains('.'))
    }
    return $false
}

function Resolve-HttpsCertificate {
    $storePath = "Cert:\$CertificateStoreLocation\$CertificateStoreName"
    if (-not (Test-Path $storePath)) { throw "Certificate store not found: $storePath" }

    if ($CertificateMode -eq 'pfx') {
        if ([string]::IsNullOrWhiteSpace($CertificatePfxPath)) { throw 'certificate.pfxPath is required when certificate.mode is pfx.' }
        $pfx = $CertificatePfxPath
        if (-not [IO.Path]::IsPathRooted($pfx)) { $pfx = Join-Path $SettingsDirectory $pfx }
        $pfx = [IO.Path]::GetFullPath($pfx)
        if (-not (Test-Path $pfx)) { throw "PFX file not found: $pfx" }

        $password = $null
        if (-not [string]::IsNullOrWhiteSpace($CertificatePfxPasswordEnvironmentVariable)) {
            $plain = [Environment]::GetEnvironmentVariable($CertificatePfxPasswordEnvironmentVariable)
            if ($null -ne $plain) { $password = ConvertTo-SecureString $plain -AsPlainText -Force }
        }
        if ($null -eq $password) {
            throw "PFX password environment variable '$CertificatePfxPasswordEnvironmentVariable' is not set."
        }

        $imported = Import-PfxCertificate -FilePath $pfx -CertStoreLocation $storePath -Password $password -Exportable:$false
        if ($null -eq $imported) { throw 'PFX certificate import failed.' }
        return $imported
    }

    if ($CertificateMode -eq 'thumbprint') {
        if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) { throw 'certificate.thumbprint is required when certificate.mode is thumbprint.' }
        $normalized = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
        $cert = Get-ChildItem $storePath | Where-Object { $_.Thumbprint -eq $normalized } | Select-Object -First 1
        if ($null -eq $cert) { throw "Certificate thumbprint not found in $storePath: $normalized" }
        if (-not $cert.HasPrivateKey) { throw 'Selected certificate does not have a private key.' }
        if ($cert.NotAfter -le (Get-Date)) { throw 'Selected certificate is expired.' }
        return $cert
    }

    $candidates = @()
    Get-ChildItem $storePath |
        Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
        ForEach-Object {
            $cert = $_
            foreach ($name in (Get-CertificateDnsNames $cert)) {
                if (Test-DnsNameMatch -Pattern $name -DnsName $WebHostName) {
                    $priority = if ([string]::Equals($name, $WebHostName, [StringComparison]::OrdinalIgnoreCase)) { 2 } else { 1 }
                    $candidates += [pscustomobject]@{ Certificate = $cert; Priority = $priority }
                    break
                }
            }
        }

    $selected = $candidates | Sort-Object Priority -Descending, @{Expression={$_.Certificate.NotAfter};Descending=$true} | Select-Object -First 1
    if ($null -eq $selected) {
        throw "No valid private-key certificate matching '$WebHostName' (exact or wildcard) was found in $storePath."
    }
    return $selected.Certificate
}

function Ensure-FirewallPort([int]$Port, [string]$ProtocolName) {
    if (-not $OpenFirewall) { return }
    $ruleName = "SQL Server Advisor $ProtocolName $Port"
    $existing = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    if ($null -eq $existing) {
        New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Profile Any | Out-Null
    }
    else {
        Enable-NetFirewallRule -DisplayName $ruleName | Out-Null
    }
}

function Configure-Iis([string]$ApiPath) {
    Write-Step 'Configuring IIS site and bindings'
    Import-Module WebAdministration

    $appPoolPath = "IIS:\AppPools\$AppPoolName"
    if (-not (Test-Path $appPoolPath)) { New-WebAppPool -Name $AppPoolName | Out-Null }
    Set-ItemProperty $appPoolPath -Name managedRuntimeVersion -Value ''
    Set-ItemProperty $appPoolPath -Name enable32BitAppOnWin64 -Value $false
    Set-ItemProperty $appPoolPath -Name processModel.identityType -Value 4
    Set-ItemProperty $appPoolPath -Name startMode -Value 'AlwaysRunning'

    $site = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
    if ($null -eq $site) {
        New-Website -Name $SiteName -PhysicalPath $ApiPath -ApplicationPool $AppPoolName -Port $HttpPort -HostHeader $WebHostName | Out-Null
    }
    else {
        Stop-Website -Name $SiteName -ErrorAction SilentlyContinue
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $ApiPath
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
    }

    Get-WebBinding -Name $SiteName -Protocol 'http' -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-WebBinding -Name $SiteName -Protocol 'http' -BindingInformation $_.bindingInformation
    }
    Get-WebBinding -Name $SiteName -Protocol 'https' -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-WebBinding -Name $SiteName -Protocol 'https' -BindingInformation $_.bindingInformation
    }

    if ($WebProtocol -eq 'http' -or $KeepHttpBinding) {
        New-WebBinding -Name $SiteName -Protocol 'http' -Port $HttpPort -HostHeader $WebHostName | Out-Null
        Ensure-FirewallPort -Port $HttpPort -ProtocolName 'HTTP'
    }

    if ($WebProtocol -eq 'https') {
        $certificate = Resolve-HttpsCertificate
        New-WebBinding -Name $SiteName -Protocol 'https' -Port $HttpsPort -HostHeader $WebHostName -SslFlags 1 | Out-Null
        $binding = Get-WebBinding -Name $SiteName -Protocol 'https' |
            Where-Object { $_.bindingInformation -eq "*:${HttpsPort}:$WebHostName" } |
            Select-Object -First 1
        if ($null -eq $binding) { throw 'HTTPS IIS binding could not be created.' }
        $binding.AddSslCertificate($certificate.Thumbprint, $CertificateStoreName)
        Ensure-FirewallPort -Port $HttpsPort -ProtocolName 'HTTPS'
        Write-Host "HTTPS certificate: $($certificate.Subject) / $($certificate.Thumbprint)"
    }

    Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $SiteName -Filter 'system.webServer/security/authentication/windowsAuthentication' -Name enabled -Value $WindowsAuthentication
    Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $SiteName -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name enabled -Value (-not $WindowsAuthentication)

    Start-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    Start-Website -Name $SiteName
}

function Configure-WorkerService([string]$WorkerPath) {
    Write-Step 'Configuring Windows Worker Service'
    $workerExe = Join-Path $WorkerPath 'SqlServerAdvisor.Worker.exe'
    if (-not (Test-Path $workerExe)) { throw "Worker executable not found: $workerExe" }

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
    if (-not $GrantApplicationIdentities) { return }

    Write-Step 'Granting SQLAdvisor permissions to application identities'
    $apiPrincipal = "IIS APPPOOL\$AppPoolName"
    $workerPrincipal = "NT SERVICE\$WorkerServiceName"
    $apiId = Escape-SqlIdentifier $apiPrincipal
    $workerId = Escape-SqlIdentifier $workerPrincipal
    $apiString = Escape-SqlString $apiPrincipal
    $workerString = Escape-SqlString $workerPrincipal
    $databaseId = Escape-SqlIdentifier $DatabaseName

    $sql = @"
USE [master];
IF SUSER_ID(N'$apiString') IS NULL CREATE LOGIN [$apiId] FROM WINDOWS;
IF SUSER_ID(N'$workerString') IS NULL CREATE LOGIN [$workerId] FROM WINDOWS;
USE [$databaseId];
IF USER_ID(N'$apiString') IS NULL CREATE USER [$apiId] FOR LOGIN [$apiId];
IF USER_ID(N'$workerString') IS NULL CREATE USER [$workerId] FOR LOGIN [$workerId];
ALTER ROLE [db_datareader] ADD MEMBER [$apiId];
ALTER ROLE [db_datawriter] ADD MEMBER [$apiId];
ALTER ROLE [db_datareader] ADD MEMBER [$workerId];
ALTER ROLE [db_datawriter] ADD MEMBER [$workerId];
GRANT EXECUTE TO [$apiId];
GRANT EXECUTE TO [$workerId];
"@

    # ALTER ROLE ADD MEMBER is not idempotent on every supported SQL version, so ignore already-member errors explicitly.
    try { Invoke-SqlQuery $sql }
    catch {
        $fallbackSql = @"
USE [$databaseId];
IF IS_ROLEMEMBER(N'db_datareader', N'$apiString') <> 1 ALTER ROLE [db_datareader] ADD MEMBER [$apiId];
IF IS_ROLEMEMBER(N'db_datawriter', N'$apiString') <> 1 ALTER ROLE [db_datawriter] ADD MEMBER [$apiId];
IF IS_ROLEMEMBER(N'db_datareader', N'$workerString') <> 1 ALTER ROLE [db_datareader] ADD MEMBER [$workerId];
IF IS_ROLEMEMBER(N'db_datawriter', N'$workerString') <> 1 ALTER ROLE [db_datawriter] ADD MEMBER [$workerId];
GRANT EXECUTE TO [$apiId];
GRANT EXECUTE TO [$workerId];
"@
        Invoke-SqlQuery $fallbackSql
    }
}

function Grant-FilePermissions([string]$ApiPath, [string]$WorkerPath) {
    Write-Step 'Configuring filesystem ACLs'
    $apiPrincipal = "IIS APPPOOL\$AppPoolName"
    $workerPrincipal = "NT SERVICE\$WorkerServiceName"
    New-Item -ItemType Directory -Force -Path $DataProtectionKeyPath | Out-Null
    Invoke-External 'icacls.exe' @($DataProtectionKeyPath, '/grant', "${apiPrincipal}:(OI)(CI)M", "${workerPrincipal}:(OI)(CI)M", '/T', '/C')
    Invoke-External 'icacls.exe' @($ApiPath, '/grant', "${apiPrincipal}:(OI)(CI)RX", '/T', '/C')
    Invoke-External 'icacls.exe' @($WorkerPath, '/grant', "${workerPrincipal}:(OI)(CI)RX", '/T', '/C')
}

function Get-AdvisorConnectionString {
    if (-not [string]::IsNullOrWhiteSpace($ConnectionStringEnvironmentVariable)) {
        $environmentConnectionString = [Environment]::GetEnvironmentVariable($ConnectionStringEnvironmentVariable)
        if (-not [string]::IsNullOrWhiteSpace($environmentConnectionString)) { return $environmentConnectionString }
    }
    if (-not [string]::IsNullOrWhiteSpace($ConfiguredConnectionString)) { return $ConfiguredConnectionString }
    return "Server=$SqlInstance;Database=$DatabaseName;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Application Name=SQLServerAdvisor"
}

function Get-PublicOrigin {
    if ($WebProtocol -eq 'https') {
        if ($HttpsPort -eq 443) { return "https://$WebHostName" }
        return "https://${WebHostName}:$HttpsPort"
    }
    if ($HttpPort -eq 80) { return "http://$WebHostName" }
    return "http://${WebHostName}:$HttpPort"
}

function Write-ProductionSettings([string]$ApiPath, [string]$WorkerPath) {
    $connectionString = Get-AdvisorConnectionString
    $origin = Get-PublicOrigin

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

function Test-ApplicationHealth {
    $url = "$(Get-PublicOrigin)/health"
    Write-Step "Checking $url"
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -UseDefaultCredentials -TimeoutSec 10
            if ($response.StatusCode -eq 200) { return $true }
        }
        catch { Start-Sleep -Seconds 2 }
    }
    return $false
}

Assert-Administrator

$SettingsPath = [IO.Path]::GetFullPath($SettingsPath)
if (-not (Test-Path $SettingsPath)) { throw "Settings file not found: $SettingsPath" }
$SettingsDirectory = Split-Path -Parent $SettingsPath
$settings = Get-Content -Raw -Path $SettingsPath | ConvertFrom-Json

$installation = Get-RequiredProperty $settings 'installation' 'root'
$web = Get-RequiredProperty $settings 'web' 'root'
$certificate = Get-RequiredProperty $settings 'certificate' 'root'
$database = Get-RequiredProperty $settings 'database' 'root'
$build = Get-RequiredProperty $settings 'build' 'root'

$InstallRoot = [IO.Path]::GetFullPath([string](Get-RequiredProperty $installation 'installRoot' 'installation'))
$SiteName = [string](Get-RequiredProperty $installation 'siteName' 'installation')
$AppPoolName = [string](Get-RequiredProperty $installation 'appPoolName' 'installation')
$WorkerServiceName = [string](Get-RequiredProperty $installation 'workerServiceName' 'installation')
$WorkerDisplayName = [string](Get-RequiredProperty $installation 'workerDisplayName' 'installation')
$DataProtectionKeyPath = [IO.Path]::GetFullPath([string](Get-RequiredProperty $installation 'dataProtectionKeyPath' 'installation'))

$WebProtocol = ([string](Get-RequiredProperty $web 'protocol' 'web')).ToLowerInvariant()
$WebHostName = [string](Get-RequiredProperty $web 'hostName' 'web')
$HttpPort = [int](Get-RequiredProperty $web 'httpPort' 'web')
$HttpsPort = [int](Get-RequiredProperty $web 'httpsPort' 'web')
$KeepHttpBinding = [bool](Get-RequiredProperty $web 'keepHttpBinding' 'web')
$WindowsAuthentication = [bool](Get-RequiredProperty $web 'windowsAuthentication' 'web')
$OpenFirewall = [bool](Get-RequiredProperty $web 'openFirewall' 'web')

$CertificateMode = ([string](Get-RequiredProperty $certificate 'mode' 'certificate')).ToLowerInvariant()
$CertificateThumbprint = [string](Get-RequiredProperty $certificate 'thumbprint' 'certificate')
$CertificatePfxPath = [string](Get-RequiredProperty $certificate 'pfxPath' 'certificate')
$CertificatePfxPasswordEnvironmentVariable = [string](Get-RequiredProperty $certificate 'pfxPasswordEnvironmentVariable' 'certificate')
$CertificateStoreLocation = [string](Get-RequiredProperty $certificate 'storeLocation' 'certificate')
$CertificateStoreName = [string](Get-RequiredProperty $certificate 'storeName' 'certificate')

$SqlInstance = [string](Get-RequiredProperty $database 'sqlInstance' 'database')
$DatabaseName = [string](Get-RequiredProperty $database 'databaseName' 'database')
$RunMigrations = [bool](Get-RequiredProperty $database 'runMigrations' 'database')
$GrantApplicationIdentities = [bool](Get-RequiredProperty $database 'grantApplicationIdentities' 'database')
$ConfiguredConnectionString = [string](Get-RequiredProperty $database 'connectionString' 'database')
$ConnectionStringEnvironmentVariable = [string](Get-RequiredProperty $database 'connectionStringEnvironmentVariable' 'database')

$BuildConfiguration = [string](Get-RequiredProperty $build 'configuration' 'build')
$InstallHostingBundleIfMissing = [bool](Get-RequiredProperty $build 'installHostingBundleIfMissing' 'build')
$HostingBundleUrl = [string](Get-RequiredProperty $build 'hostingBundleUrl' 'build')

if ($WebProtocol -notin @('http','https')) { throw 'web.protocol must be http or https.' }
if ($CertificateMode -notin @('auto','thumbprint','pfx')) { throw 'certificate.mode must be auto, thumbprint or pfx.' }
if ([string]::IsNullOrWhiteSpace($WebHostName)) { throw 'web.hostName cannot be empty.' }
if ($RunMigrations -and $DatabaseName -ne 'SQLAdvisor') {
    throw "The bundled migration scripts currently create the SQLAdvisor database. Set database.databaseName to SQLAdvisor or set runMigrations=false."
}

$SourceRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$apiProject = Join-Path $SourceRoot 'backend\src\SqlServerAdvisor.Api\SqlServerAdvisor.Api.csproj'
$workerProject = Join-Path $SourceRoot 'backend\src\SqlServerAdvisor.Worker\SqlServerAdvisor.Worker.csproj'
$frontendRoot = Join-Path $SourceRoot 'frontend'
$databaseRoot = Join-Path $SourceRoot 'database'

Write-Step "Using settings: $SettingsPath"
Write-Host "Install root : $InstallRoot"
Write-Host "Public URL   : $(Get-PublicOrigin)"
Write-Host "SQL instance : $SqlInstance"
Write-Host "IIS site     : $SiteName"
Write-Host "Worker       : $WorkerServiceName"

Require-Command 'dotnet.exe' '.NET 10 SDK is required.'
Require-Command 'node.exe' 'Node.js 24 or later is required.'
Require-Command 'npm.cmd' 'npm is required.'
if (-not (Test-DotNet10Sdk)) { throw '.NET SDK major version 10 is required.' }
if (-not (Test-Node24)) { throw 'Node.js 24 or later is required.' }

Ensure-Iis
Ensure-HostingBundle
if ($RunMigrations) { Require-Command 'sqlcmd.exe' 'Microsoft sqlcmd must be installed and available in PATH.' }

$stageRoot = Join-Path $env:TEMP ("SqlServerAdvisor-Install-" + [guid]::NewGuid().ToString('N'))
$apiStage = Join-Path $stageRoot 'Api'
$workerStage = Join-Path $stageRoot 'Worker'
$apiPath = Join-Path $InstallRoot 'Api'
$workerPath = Join-Path $InstallRoot 'Worker'

try {
    New-Item -ItemType Directory -Force -Path $apiStage, $workerStage | Out-Null

    Write-Step '.NET API publish'
    Invoke-External 'dotnet.exe' @('publish', $apiProject, '-c', $BuildConfiguration, '-o', $apiStage)

    Write-Step '.NET Worker publish'
    Invoke-External 'dotnet.exe' @('publish', $workerProject, '-c', $BuildConfiguration, '-o', $workerStage)

    Write-Step 'Angular production build'
    Push-Location $frontendRoot
    try {
        if (Test-Path (Join-Path $frontendRoot 'package-lock.json')) { Invoke-External 'npm.cmd' @('ci', '--no-audit', '--no-fund') }
        else { Invoke-External 'npm.cmd' @('install', '--no-audit', '--no-fund') }
        Invoke-External 'npm.cmd' @('run', 'build')
    }
    finally { Pop-Location }

    $browserPath = Join-Path $frontendRoot 'dist\sql-server-advisor-ui\browser'
    if (-not (Test-Path (Join-Path $browserPath 'index.html'))) { throw "Angular output not found: $browserPath" }
    $wwwroot = Join-Path $apiStage 'wwwroot'
    New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
    Copy-Item -Path (Join-Path $browserPath '*') -Destination $wwwroot -Recurse -Force

    $existingService = Get-Service -Name $WorkerServiceName -ErrorAction SilentlyContinue
    if ($existingService) { Stop-Service -Name $WorkerServiceName -Force -ErrorAction SilentlyContinue }
    if (Get-Module -ListAvailable WebAdministration) {
        Import-Module WebAdministration
        if (Get-Website -Name $SiteName -ErrorAction SilentlyContinue) { Stop-Website -Name $SiteName -ErrorAction SilentlyContinue }
    }

    New-Item -ItemType Directory -Force -Path $apiPath, $workerPath | Out-Null
    Remove-Item -Path (Join-Path $apiPath '*') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $workerPath '*') -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item -Path (Join-Path $apiStage '*') -Destination $apiPath -Recurse -Force
    Copy-Item -Path (Join-Path $workerStage '*') -Destination $workerPath -Recurse -Force

    Write-ProductionSettings -ApiPath $apiPath -WorkerPath $workerPath
    Configure-Iis -ApiPath $apiPath
    Configure-WorkerService -WorkerPath $workerPath

    if ($RunMigrations) {
        Write-Step 'Running application database migrations'
        $migrationFiles = Get-ChildItem -Path $databaseRoot -Filter '*.sql' -File |
            Where-Object { $_.Name -notmatch 'template' } |
            Sort-Object Name
        if ($migrationFiles.Count -eq 0) { throw 'No database migration scripts were found.' }
        foreach ($migration in $migrationFiles) { Invoke-SqlFile $migration.FullName }
        Grant-ApplicationDatabasePermissions
    }

    Grant-FilePermissions -ApiPath $apiPath -WorkerPath $workerPath
    Start-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    Start-Website -Name $SiteName -ErrorAction SilentlyContinue
    Start-Service -Name $WorkerServiceName

    if (-not (Test-ApplicationHealth)) {
        Write-Warning "Installation completed, but the public health check failed. Verify DNS, certificate trust and IIS binding for $(Get-PublicOrigin)."
    }

    Write-Host "`nSQL Server Advisor installation completed." -ForegroundColor Green
    Write-Host "URL          : $(Get-PublicOrigin)"
    Write-Host "API path     : $apiPath"
    Write-Host "Worker path  : $workerPath"
    Write-Host "Settings     : $SettingsPath"
    Write-Host "Database     : $DatabaseName @ $SqlInstance"
}
finally {
    if (Test-Path $stageRoot) { Remove-Item -Path $stageRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
