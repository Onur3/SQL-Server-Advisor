[CmdletBinding()]
param(
    [string]$SettingsPath = (Join-Path $PSScriptRoot 'install.settings.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This script must be run from an elevated PowerShell session.'
    }
}

function Get-CertificateDnsNames([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate) {
    $names = @()
    try { $names += @($Certificate.DnsNameList | ForEach-Object { $_.Unicode }) } catch { }
    if ($names.Count -eq 0 -and $Certificate.Subject -match '(?:^|,\s*)CN=([^,]+)') { $names += $Matches[1].Trim() }
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

function Resolve-Certificate {
    $storePath = "Cert:\$StoreLocation\$StoreName"
    if ($Mode -eq 'pfx') {
        $path = $PfxPath
        if (-not [IO.Path]::IsPathRooted($path)) { $path = Join-Path $SettingsDirectory $path }
        $path = [IO.Path]::GetFullPath($path)
        if (-not (Test-Path $path)) { throw "PFX file not found: $path" }
        $plain = [Environment]::GetEnvironmentVariable($PfxPasswordEnvironmentVariable)
        if ($null -eq $plain) { throw "PFX password environment variable '$PfxPasswordEnvironmentVariable' is not set." }
        $secure = ConvertTo-SecureString $plain -AsPlainText -Force
        return Import-PfxCertificate -FilePath $path -CertStoreLocation $storePath -Password $secure -Exportable:$false
    }

    if ($Mode -eq 'thumbprint') {
        $normalized = $Thumbprint.Replace(' ', '').ToUpperInvariant()
        $cert = Get-ChildItem $storePath | Where-Object { $_.Thumbprint -eq $normalized } | Select-Object -First 1
        if ($null -eq $cert) { throw "Certificate not found: $normalized" }
        return $cert
    }

    $candidates = @()
    Get-ChildItem $storePath |
        Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
        ForEach-Object {
            $cert = $_
            foreach ($name in (Get-CertificateDnsNames $cert)) {
                if (Test-DnsNameMatch -Pattern $name -DnsName $HostName) {
                    $priority = if ([string]::Equals($name, $HostName, [StringComparison]::OrdinalIgnoreCase)) { 2 } else { 1 }
                    $candidates += [pscustomobject]@{ Certificate = $cert; Priority = $priority }
                    break
                }
            }
        }
    $selected = $candidates | Sort-Object Priority -Descending, @{Expression={$_.Certificate.NotAfter};Descending=$true} | Select-Object -First 1
    if ($null -eq $selected) { throw "No valid certificate matching '$HostName' was found in $storePath." }
    return $selected.Certificate
}

Assert-Administrator
$SettingsPath = [IO.Path]::GetFullPath($SettingsPath)
if (-not (Test-Path $SettingsPath)) { throw "Settings file not found: $SettingsPath" }
$SettingsDirectory = Split-Path -Parent $SettingsPath
$settings = Get-Content -Raw -Path $SettingsPath | ConvertFrom-Json

$SiteName = [string]$settings.installation.siteName
$HostName = [string]$settings.web.hostName
$HttpsPort = [int]$settings.web.httpsPort
$KeepHttpBinding = [bool]$settings.web.keepHttpBinding
$OpenFirewall = [bool]$settings.web.openFirewall
$Mode = ([string]$settings.certificate.mode).ToLowerInvariant()
$Thumbprint = [string]$settings.certificate.thumbprint
$PfxPath = [string]$settings.certificate.pfxPath
$PfxPasswordEnvironmentVariable = [string]$settings.certificate.pfxPasswordEnvironmentVariable
$StoreLocation = [string]$settings.certificate.storeLocation
$StoreName = [string]$settings.certificate.storeName

if (([string]$settings.web.protocol).ToLowerInvariant() -ne 'https') {
    throw 'web.protocol is not https. Change deploy/install.settings.json before configuring HTTPS.'
}
if ($Mode -notin @('auto','thumbprint','pfx')) { throw 'certificate.mode must be auto, thumbprint or pfx.' }

Import-Module WebAdministration
$site = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
if ($null -eq $site) { throw "IIS site not found: $SiteName" }

$certificate = Resolve-Certificate
if ($null -eq $certificate -or -not $certificate.HasPrivateKey) { throw 'Resolved certificate is invalid or does not contain a private key.' }
if ($certificate.NotAfter -le (Get-Date)) { throw 'Resolved certificate is expired.' }

Get-WebBinding -Name $SiteName -Protocol 'https' -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-WebBinding -Name $SiteName -Protocol 'https' -BindingInformation $_.bindingInformation
}
New-WebBinding -Name $SiteName -Protocol 'https' -Port $HttpsPort -HostHeader $HostName -SslFlags 1 | Out-Null
$binding = Get-WebBinding -Name $SiteName -Protocol 'https' |
    Where-Object { $_.bindingInformation -eq "*:${HttpsPort}:$HostName" } |
    Select-Object -First 1
if ($null -eq $binding) { throw 'HTTPS IIS binding could not be created.' }
$binding.AddSslCertificate($certificate.Thumbprint, $StoreName)

if (-not $KeepHttpBinding) {
    Get-WebBinding -Name $SiteName -Protocol 'http' -ErrorAction SilentlyContinue | ForEach-Object {
        Remove-WebBinding -Name $SiteName -Protocol 'http' -BindingInformation $_.bindingInformation
    }
}

if ($OpenFirewall) {
    $ruleName = "SQL Server Advisor HTTPS $HttpsPort"
    if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $HttpsPort -Profile Any | Out-Null
    }
    else { Enable-NetFirewallRule -DisplayName $ruleName | Out-Null }
}

Restart-WebAppPool -Name $site.applicationPool -ErrorAction SilentlyContinue
Start-Website -Name $SiteName -ErrorAction SilentlyContinue

Write-Host "HTTPS binding updated from settings." -ForegroundColor Green
Write-Host "URL        : https://${HostName}:$HttpsPort"
Write-Host "Certificate: $($certificate.Thumbprint)"
Write-Host "Settings   : $SettingsPath"
