[CmdletBinding()]
param(
    [string]$SettingsPath = (Join-Path $PSScriptRoot 'install.settings.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Step([string]$Text) { Write-Host "`n==> $Text" -ForegroundColor Cyan }

function Assert-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this installer from an elevated PowerShell session.'
    }
}

function Require([string]$Name, [string]$Message) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) { throw "$Name not found. $Message" }
}

function Run([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code $LASTEXITCODE." }
}

function Escape-Id([string]$Value) { $Value.Replace(']', ']]') }
function Escape-String([string]$Value) { $Value.Replace("'", "''") }

function Get-DnsNames([System.Security.Cryptography.X509Certificates.X509Certificate2]$Cert) {
    $names = @()
    try { $names += @($Cert.DnsNameList | ForEach-Object { $_.Unicode }) } catch { }
    if ($names.Count -eq 0 -and $Cert.Subject -match '(?:^|,\s*)CN=([^,]+)') { $names += $Matches[1].Trim() }
    @($names | Where-Object { $_ } | Select-Object -Unique)
}

function Match-Dns([string]$Pattern, [string]$DnsName) {
    if ([string]::Equals($Pattern, $DnsName, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if (-not $Pattern.StartsWith('*.')) { return $false }
    $suffix = $Pattern.Substring(1)
    if (-not $DnsName.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)) { return $false }
    $left = $DnsName.Substring(0, $DnsName.Length - $suffix.Length)
    return $left -and (-not $left.Contains('.'))
}

function Resolve-Certificate {
    $storePath = "Cert:\$CertStoreLocation\$CertStoreName"
    if (-not (Test-Path $storePath)) { throw "Certificate store not found: $storePath" }

    if ($CertMode -eq 'pfx') {
        if (-not $CertPfxPath) { throw 'certificate.pfxPath is required for pfx mode.' }
        $path = $CertPfxPath
        if (-not [IO.Path]::IsPathRooted($path)) { $path = Join-Path $SettingsDirectory $path }
        $path = [IO.Path]::GetFullPath($path)
        if (-not (Test-Path $path)) { throw "PFX file not found: $path" }
        $plain = [Environment]::GetEnvironmentVariable($CertPfxPasswordEnv)
        if ($null -eq $plain) { throw "Environment variable '$CertPfxPasswordEnv' is required for the PFX password." }
        $secure = ConvertTo-SecureString $plain -AsPlainText -Force
        $imported = Import-PfxCertificate -FilePath $path -CertStoreLocation $storePath -Password $secure -Exportable:$false
        if ($null -eq $imported) { throw 'PFX import failed.' }
        return $imported
    }

    if ($CertMode -eq 'thumbprint') {
        $thumb = $CertThumbprint.Replace(' ', '').ToUpperInvariant()
        $cert = Get-ChildItem $storePath | Where-Object { $_.Thumbprint -eq $thumb } | Select-Object -First 1
        if ($null -eq $cert) { throw "Certificate not found: $thumb" }
        if (-not $cert.HasPrivateKey -or $cert.NotAfter -le (Get-Date)) { throw 'Selected certificate is invalid, expired or has no private key.' }
        return $cert
    }

    $items = @()
    Get-ChildItem $storePath | Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } | ForEach-Object {
        $cert = $_
        foreach ($name in (Get-DnsNames $cert)) {
            if (Match-Dns $name $HostName) {
                $priority = if ([string]::Equals($name, $HostName, [StringComparison]::OrdinalIgnoreCase)) { 2 } else { 1 }
                $items += [pscustomobject]@{ Certificate = $cert; Priority = $priority }
                break
            }
        }
    }
    $selected = $items | Sort-Object -Property @{Expression='Priority';Descending=$true}, @{Expression={$_.Certificate.NotAfter};Descending=$true} | Select-Object -First 1
    if ($null -eq $selected) { throw "No valid exact-name or wildcard certificate was found for '$HostName'." }
    $selected.Certificate
}

function Ensure-Iis {
    Step 'Checking IIS'
    Import-Module ServerManager
    $features = @('Web-Server','Web-Static-Content','Web-Default-Doc','Web-Http-Errors','Web-Http-Logging','Web-Request-Monitor','Web-Filtering','Web-Mgmt-Console')
    if ($WindowsAuth) { $features += 'Web-Windows-Auth' }
    $missing = @($features | Where-Object { -not (Get-WindowsFeature $_).Installed })
    if ($missing.Count -gt 0) {
        $result = Install-WindowsFeature -Name $missing -IncludeManagementTools
        if (-not $result.Success) { throw 'IIS feature installation failed.' }
    }
}

function Ensure-HostingBundle {
    $moduleExists = (Test-Path "$env:ProgramFiles\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll") -or (Test-Path "${env:ProgramFiles(x86)}\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll")
    if ($moduleExists) { return }
    if (-not $InstallHostingBundle) { throw 'ASP.NET Core Module V2 is missing.' }

    Step 'Installing .NET 10 Hosting Bundle'
    $url = $HostingBundleUrl
    if (-not $url) {
        $page = Invoke-WebRequest 'https://dotnet.microsoft.com/permalink/dotnetcore-current-windows-runtime-bundle-installer' -UseBasicParsing
        $m = [regex]::Match($page.Content, 'https://builds\.dotnet\.microsoft\.com/[^"''<> ]+/dotnet-hosting-10\.[^"''<> ]+-win\.exe')
        if (-not $m.Success) { throw 'Set build.hostingBundleUrl explicitly.' }
        $url = $m.Value
    }
    $exe = Join-Path $env:TEMP 'dotnet-hosting-10-win.exe'
    Invoke-WebRequest $url -OutFile $exe -UseBasicParsing
    $proc = Start-Process $exe -ArgumentList '/install','/quiet','/norestart' -Wait -PassThru
    if ($proc.ExitCode -notin @(0,3010)) { throw "Hosting Bundle failed: $($proc.ExitCode)" }
}

function Open-Port([int]$Port, [string]$Label) {
    if (-not $OpenFirewall) { return }
    $name = "SQL Server Advisor $Label $Port"
    if (-not (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Profile Any | Out-Null
    }
    else { Enable-NetFirewallRule -DisplayName $name | Out-Null }
}

function Configure-Iis([string]$ApiPath) {
    Step 'Configuring IIS site'
    Import-Module WebAdministration
    $poolPath = "IIS:\AppPools\$AppPoolName"
    if (-not (Test-Path $poolPath)) { New-WebAppPool $AppPoolName | Out-Null }
    Set-ItemProperty $poolPath -Name managedRuntimeVersion -Value ''
    Set-ItemProperty $poolPath -Name enable32BitAppOnWin64 -Value $false
    Set-ItemProperty $poolPath -Name processModel.identityType -Value 4
    Set-ItemProperty $poolPath -Name startMode -Value 'AlwaysRunning'

    if (-not (Get-Website -Name $SiteName -ErrorAction SilentlyContinue)) {
        New-Website -Name $SiteName -PhysicalPath $ApiPath -ApplicationPool $AppPoolName -Port $HttpPort -HostHeader $HostName | Out-Null
    }
    else {
        Stop-Website $SiteName -ErrorAction SilentlyContinue
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $ApiPath
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
    }

    Get-WebBinding -Name $SiteName -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-WebBinding -Name $SiteName -Protocol $_.protocol -BindingInformation $_.bindingInformation
    }

    if ($Protocol -eq 'http' -or $KeepHttp) {
        New-WebBinding -Name $SiteName -Protocol http -Port $HttpPort -HostHeader $HostName | Out-Null
        Open-Port $HttpPort 'HTTP'
    }
    if ($Protocol -eq 'https') {
        $cert = Resolve-Certificate
        New-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort -HostHeader $HostName -SslFlags 1 | Out-Null
        $binding = Get-WebBinding -Name $SiteName -Protocol https | Where-Object { $_.bindingInformation -eq "*:${HttpsPort}:$HostName" } | Select-Object -First 1
        if ($null -eq $binding) { throw 'HTTPS binding creation failed.' }
        $binding.AddSslCertificate($cert.Thumbprint, $CertStoreName)
        Open-Port $HttpsPort 'HTTPS'
        Write-Host "Certificate: $($cert.Subject) / $($cert.Thumbprint)"
    }

    Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $SiteName -Filter 'system.webServer/security/authentication/windowsAuthentication' -Name enabled -Value $WindowsAuth
    Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $SiteName -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name enabled -Value (-not $WindowsAuth)
    Start-WebAppPool $AppPoolName -ErrorAction SilentlyContinue
    Start-Website $SiteName
}

function Configure-Worker([string]$WorkerPath) {
    Step 'Configuring Worker service'
    $exe = Join-Path $WorkerPath 'SqlServerAdvisor.Worker.exe'
    if (-not (Test-Path $exe)) { throw "Worker executable not found: $exe" }
    $service = Get-Service $WorkerServiceName -ErrorAction SilentlyContinue
    if ($service) {
        Stop-Service $WorkerServiceName -Force -ErrorAction SilentlyContinue
        Run sc.exe @('config',$WorkerServiceName,'binPath=',"`"$exe`"",'start=','auto','obj=',"NT SERVICE\$WorkerServiceName")
    }
    else {
        Run sc.exe @('create',$WorkerServiceName,'binPath=',"`"$exe`"",'DisplayName=',$WorkerDisplayName,'start=','auto','obj=',"NT SERVICE\$WorkerServiceName")
    }
    Run sc.exe @('failure',$WorkerServiceName,'reset=','86400','actions=','restart/5000/restart/10000/restart/30000')
    Run sc.exe @('sidtype',$WorkerServiceName,'unrestricted')
}

function Get-ConnectionString {
    if ($ConnectionStringEnv) {
        $fromEnv = [Environment]::GetEnvironmentVariable($ConnectionStringEnv)
        if ($fromEnv) { return $fromEnv }
    }
    if ($ConfiguredConnectionString) { return $ConfiguredConnectionString }
    "Server=$SqlInstance;Database=$DatabaseName;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Application Name=SQLServerAdvisor"
}

function Public-Origin {
    if ($Protocol -eq 'https') {
        if ($HttpsPort -eq 443) { return "https://$HostName" }
        return "https://${HostName}:$HttpsPort"
    }
    if ($HttpPort -eq 80) { return "http://$HostName" }
    "http://${HostName}:$HttpPort"
}

function Write-AppSettings([string]$ApiPath, [string]$WorkerPath) {
    $connection = Get-ConnectionString
    $origin = Public-Origin
    [ordered]@{
        ConnectionStrings = @{ AdvisorDatabase = $connection }
        Security = @{ DataProtectionKeyPath = $KeyPath }
        HttpsRedirection = @{ Enabled = $false }
        Cors = @{ Origins = @($origin) }
        Logging = @{ LogLevel = @{ Default='Information'; 'Microsoft.AspNetCore'='Warning' } }
        AllowedHosts = '*'
    } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $ApiPath 'appsettings.Production.json') -Encoding UTF8

    [ordered]@{
        ConnectionStrings = @{ AdvisorDatabase = $connection }
        Security = @{ DataProtectionKeyPath = $KeyPath }
        Logging = @{ LogLevel = @{ Default='Information'; 'Microsoft.Hosting.Lifetime'='Information' } }
    } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $WorkerPath 'appsettings.Production.json') -Encoding UTF8
}

function Run-Migrations {
    if (-not $RunMigrations) { return }
    Require sqlcmd.exe 'Install Microsoft sqlcmd and add it to PATH.'
    if ($DatabaseName -ne 'SQLAdvisor') { throw 'Bundled migrations currently create the SQLAdvisor database.' }
    Step 'Running database migrations'
    Get-ChildItem $DatabaseRoot -Filter '*.sql' -File | Where-Object { $_.Name -notmatch 'template' } | Sort-Object Name | ForEach-Object {
        Run sqlcmd.exe @('-S',$SqlInstance,'-E','-C','-b','-i',$_.FullName)
    }
}

function Grant-DbIdentities {
    if (-not $RunMigrations -or -not $GrantIdentities) { return }
    $api = "IIS APPPOOL\$AppPoolName"
    $worker = "NT SERVICE\$WorkerServiceName"
    $apiId = Escape-Id $api; $workerId = Escape-Id $worker
    $apiString = Escape-String $api; $workerString = Escape-String $worker
    $dbId = Escape-Id $DatabaseName
    $sql = @"
USE [master];
IF SUSER_ID(N'$apiString') IS NULL CREATE LOGIN [$apiId] FROM WINDOWS;
IF SUSER_ID(N'$workerString') IS NULL CREATE LOGIN [$workerId] FROM WINDOWS;
USE [$dbId];
IF USER_ID(N'$apiString') IS NULL CREATE USER [$apiId] FOR LOGIN [$apiId];
IF USER_ID(N'$workerString') IS NULL CREATE USER [$workerId] FOR LOGIN [$workerId];
IF IS_ROLEMEMBER(N'db_datareader', N'$apiString') <> 1 ALTER ROLE [db_datareader] ADD MEMBER [$apiId];
IF IS_ROLEMEMBER(N'db_datawriter', N'$apiString') <> 1 ALTER ROLE [db_datawriter] ADD MEMBER [$apiId];
IF IS_ROLEMEMBER(N'db_datareader', N'$workerString') <> 1 ALTER ROLE [db_datareader] ADD MEMBER [$workerId];
IF IS_ROLEMEMBER(N'db_datawriter', N'$workerString') <> 1 ALTER ROLE [db_datawriter] ADD MEMBER [$workerId];
GRANT EXECUTE TO [$apiId];
GRANT EXECUTE TO [$workerId];
"@
    Run sqlcmd.exe @('-S',$SqlInstance,'-E','-C','-b','-Q',$sql)
}

function Grant-Acl([string]$ApiPath, [string]$WorkerPath) {
    New-Item -ItemType Directory -Force $KeyPath | Out-Null
    $api = "IIS APPPOOL\$AppPoolName"
    $worker = "NT SERVICE\$WorkerServiceName"
    Run icacls.exe @($KeyPath,'/grant',"${api}:(OI)(CI)M","${worker}:(OI)(CI)M",'/T','/C')
    Run icacls.exe @($ApiPath,'/grant',"${api}:(OI)(CI)RX",'/T','/C')
    Run icacls.exe @($WorkerPath,'/grant',"${worker}:(OI)(CI)RX",'/T','/C')
}

Assert-Admin
$SettingsPath = [IO.Path]::GetFullPath($SettingsPath)
if (-not (Test-Path $SettingsPath)) { throw "Settings not found: $SettingsPath" }
$SettingsDirectory = Split-Path -Parent $SettingsPath
$s = Get-Content -Raw -Encoding UTF8 $SettingsPath | ConvertFrom-Json

$InstallRoot = [IO.Path]::GetFullPath([string]$s.installation.installRoot)
$SiteName = [string]$s.installation.siteName
$AppPoolName = [string]$s.installation.appPoolName
$WorkerServiceName = [string]$s.installation.workerServiceName
$WorkerDisplayName = [string]$s.installation.workerDisplayName
$KeyPath = [IO.Path]::GetFullPath([string]$s.installation.dataProtectionKeyPath)
$Protocol = ([string]$s.web.protocol).ToLowerInvariant()
$HostName = [string]$s.web.hostName
$HttpPort = [int]$s.web.httpPort
$HttpsPort = [int]$s.web.httpsPort
$KeepHttp = [bool]$s.web.keepHttpBinding
$WindowsAuth = [bool]$s.web.windowsAuthentication
$OpenFirewall = [bool]$s.web.openFirewall
$CertMode = ([string]$s.certificate.mode).ToLowerInvariant()
$CertThumbprint = [string]$s.certificate.thumbprint
$CertPfxPath = [string]$s.certificate.pfxPath
$CertPfxPasswordEnv = [string]$s.certificate.pfxPasswordEnvironmentVariable
$CertStoreLocation = [string]$s.certificate.storeLocation
$CertStoreName = [string]$s.certificate.storeName
$SqlInstance = [string]$s.database.sqlInstance
$DatabaseName = [string]$s.database.databaseName
$RunMigrations = [bool]$s.database.runMigrations
$GrantIdentities = [bool]$s.database.grantApplicationIdentities
$ConfiguredConnectionString = [string]$s.database.connectionString
$ConnectionStringEnv = [string]$s.database.connectionStringEnvironmentVariable
$BuildConfiguration = [string]$s.build.configuration
$InstallHostingBundle = [bool]$s.build.installHostingBundleIfMissing
$HostingBundleUrl = [string]$s.build.hostingBundleUrl

if ($Protocol -notin @('http','https')) { throw 'web.protocol must be http or https.' }
if ($CertMode -notin @('auto','thumbprint','pfx')) { throw 'certificate.mode must be auto, thumbprint or pfx.' }
if (-not $HostName) { throw 'web.hostName is required.' }

$SourceRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$ApiProject = Join-Path $SourceRoot 'backend\src\SqlServerAdvisor.Api\SqlServerAdvisor.Api.csproj'
$WorkerProject = Join-Path $SourceRoot 'backend\src\SqlServerAdvisor.Worker\SqlServerAdvisor.Worker.csproj'
$FrontendRoot = Join-Path $SourceRoot 'frontend'
$DatabaseRoot = Join-Path $SourceRoot 'database'

Step 'Validating prerequisites'
Require dotnet.exe '.NET 10 SDK is required.'
Require node.exe 'Node.js 24 or later is required.'
Require npm.cmd 'npm is required.'
if (([version]((& dotnet --version).Split('-')[0])).Major -ne 10) { throw '.NET SDK 10 is required.' }
if (([version]((& node --version).TrimStart('v').Split('-')[0])).Major -lt 24) { throw 'Node.js 24 or later is required.' }
Ensure-Iis
Ensure-HostingBundle

$stage = Join-Path $env:TEMP ("SqlServerAdvisor-" + [guid]::NewGuid().ToString('N'))
$apiStage = Join-Path $stage 'Api'; $workerStage = Join-Path $stage 'Worker'
$apiPath = Join-Path $InstallRoot 'Api'; $workerPath = Join-Path $InstallRoot 'Worker'
try {
    New-Item -ItemType Directory -Force $apiStage,$workerStage | Out-Null
    Step 'Publishing API'; Run dotnet.exe @('publish',$ApiProject,'-c',$BuildConfiguration,'-o',$apiStage)
    Step 'Publishing Worker'; Run dotnet.exe @('publish',$WorkerProject,'-c',$BuildConfiguration,'-o',$workerStage)
    Step 'Building Angular'
    Push-Location $FrontendRoot
    try {
        if (Test-Path 'package-lock.json') { Run npm.cmd @('ci','--no-audit','--no-fund') } else { Run npm.cmd @('install','--no-audit','--no-fund') }
        Run npm.cmd @('run','build')
    }
    finally { Pop-Location }

    $browser = Join-Path $FrontendRoot 'dist\sql-server-advisor-ui\browser'
    if (-not (Test-Path (Join-Path $browser 'index.html'))) { throw "Angular output not found: $browser" }
    New-Item -ItemType Directory -Force (Join-Path $apiStage 'wwwroot') | Out-Null
    Copy-Item (Join-Path $browser '*') (Join-Path $apiStage 'wwwroot') -Recurse -Force

    if (Get-Service $WorkerServiceName -ErrorAction SilentlyContinue) { Stop-Service $WorkerServiceName -Force -ErrorAction SilentlyContinue }
    Import-Module WebAdministration
    if (Get-Website $SiteName -ErrorAction SilentlyContinue) { Stop-Website $SiteName -ErrorAction SilentlyContinue }

    New-Item -ItemType Directory -Force $apiPath,$workerPath | Out-Null
    Remove-Item (Join-Path $apiPath '*') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $workerPath '*') -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $apiStage '*') $apiPath -Recurse -Force
    Copy-Item (Join-Path $workerStage '*') $workerPath -Recurse -Force

    Write-AppSettings $apiPath $workerPath
    Configure-Iis $apiPath
    Configure-Worker $workerPath
    Run-Migrations
    Grant-DbIdentities
    Grant-Acl $apiPath $workerPath
    Start-WebAppPool $AppPoolName -ErrorAction SilentlyContinue
    Start-Website $SiteName -ErrorAction SilentlyContinue
    Start-Service $WorkerServiceName

    $url = "$(Public-Origin)/health"
    try {
        $r = Invoke-WebRequest $url -UseDefaultCredentials -UseBasicParsing -TimeoutSec 10
        if ($r.StatusCode -ne 200) { Write-Warning "Health check returned $($r.StatusCode)." }
    }
    catch { Write-Warning "Health check could not reach $url. Verify DNS and certificate trust." }

    Write-Host "`nInstallation completed." -ForegroundColor Green
    Write-Host "URL         : $(Public-Origin)"
    Write-Host "API path    : $apiPath"
    Write-Host "Worker path : $workerPath"
    Write-Host "Settings    : $SettingsPath"
}
finally {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue }
}
