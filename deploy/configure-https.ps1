[CmdletBinding()]
param(
    [string]$SiteName = 'SQLServerAdvisor',
    [Parameter(Mandatory=$true)][string]$HostName,
    [int]$HttpsPort = 443,
    [string]$CertificateThumbprint = '',
    [string]$PfxPath = '',
    [SecureString]$PfxPassword,
    [switch]$RemoveHttpBinding
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Bu script Administrator olarak calistirilmalidir.'
    }
}

function Get-CertificateDnsNames([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate) {
    $names = @()
    try {
        $names += @($Certificate.DnsNameList | ForEach-Object { $_.Unicode })
    }
    catch { }

    if ($names.Count -eq 0 -and $Certificate.Subject -match '(?:^|,\s*)CN=([^,]+)') {
        $names += $Matches[1].Trim()
    }

    return @($names | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
}

function Test-DnsNameMatch([string]$Pattern, [string]$DnsName) {
    if ([string]::Equals($Pattern, $DnsName, [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    if ($Pattern.StartsWith('*.')) {
        $suffix = $Pattern.Substring(1)
        if (-not $DnsName.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }

        $left = $DnsName.Substring(0, $DnsName.Length - $suffix.Length)
        return (-not [string]::IsNullOrWhiteSpace($left)) -and (-not $left.Contains('.'))
    }

    return $false
}

function Resolve-Certificate {
    if (-not [string]::IsNullOrWhiteSpace($PfxPath)) {
        $resolvedPfx = [IO.Path]::GetFullPath($PfxPath)
        if (-not (Test-Path $resolvedPfx)) {
            throw "PFX dosyasi bulunamadi: $resolvedPfx"
        }

        if ($null -eq $PfxPassword) {
            $script:PfxPassword = Read-Host 'PFX parolasini girin' -AsSecureString
        }

        $imported = Import-PfxCertificate -FilePath $resolvedPfx -CertStoreLocation 'Cert:\LocalMachine\My' -Password $PfxPassword -Exportable:$false
        if ($null -eq $imported) {
            throw 'PFX sertifikasi LocalMachine\\My store icine import edilemedi.'
        }
        $script:CertificateThumbprint = $imported.Thumbprint
    }

    if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $normalized = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
        $cert = Get-ChildItem 'Cert:\LocalMachine\My' | Where-Object { $_.Thumbprint -eq $normalized } | Select-Object -First 1
        if ($null -eq $cert) {
            throw "CertificateThumbprint LocalMachine\\My store icinde bulunamadi: $normalized"
        }
        if (-not $cert.HasPrivateKey) { throw 'Secilen sertifikanin private key bilgisi yok.' }
        if ($cert.NotAfter -le (Get-Date)) { throw "Secilen sertifika suresi dolmus: $($cert.NotAfter)" }
        return $cert
    }

    $matches = Get-ChildItem 'Cert:\LocalMachine\My' |
        Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
        ForEach-Object {
            $cert = $_
            $matched = $false
            foreach ($name in (Get-CertificateDnsNames $cert)) {
                if (Test-DnsNameMatch -Pattern $name -DnsName $HostName) {
                    $matched = $true
                    break
                }
            }
            if ($matched) { $cert }
        } |
        Sort-Object NotAfter -Descending

    $selected = @($matches) | Select-Object -First 1
    if ($null -eq $selected) {
        throw "'$HostName' veya uygun wildcard icin LocalMachine\\My store icinde private-key sahibi gecerli sertifika bulunamadi. -CertificateThumbprint veya -PfxPath kullanin."
    }

    return $selected
}

Assert-Administrator
Import-Module WebAdministration

$site = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
if ($null -eq $site) {
    throw "IIS site bulunamadi: $SiteName. Once deploy\\install.ps1 calistirin."
}

$certificate = Resolve-Certificate
Write-Host "Sertifika : $($certificate.Subject)" -ForegroundColor Cyan
Write-Host "Thumbprint: $($certificate.Thumbprint)"
Write-Host "Gecerlilik: $($certificate.NotBefore) - $($certificate.NotAfter)"

Get-WebBinding -Name $SiteName -Protocol 'https' -ErrorAction SilentlyContinue |
    Where-Object { $_.bindingInformation -eq "*:${HttpsPort}:$HostName" } |
    ForEach-Object {
        Remove-WebBinding -Name $SiteName -Protocol 'https' -BindingInformation $_.bindingInformation
    }

New-WebBinding -Name $SiteName -Protocol 'https' -Port $HttpsPort -HostHeader $HostName -SslFlags 1 | Out-Null
$binding = Get-WebBinding -Name $SiteName -Protocol 'https' |
    Where-Object { $_.bindingInformation -eq "*:${HttpsPort}:$HostName" } |
    Select-Object -First 1

if ($null -eq $binding) {
    throw 'HTTPS IIS binding olusturulamadi.'
}

$binding.AddSslCertificate($certificate.Thumbprint, 'My')

$firewallRuleName = "SQL Server Advisor HTTPS $HttpsPort"
$firewallRule = Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue
if ($null -eq $firewallRule) {
    New-NetFirewallRule -DisplayName $firewallRuleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $HttpsPort -Profile Any | Out-Null
}
else {
    Enable-NetFirewallRule -DisplayName $firewallRuleName | Out-Null
}

if ($RemoveHttpBinding) {
    Get-WebBinding -Name $SiteName -Protocol 'http' -ErrorAction SilentlyContinue |
        ForEach-Object {
            Remove-WebBinding -Name $SiteName -Protocol 'http' -BindingInformation $_.bindingInformation
        }
}

Restart-WebAppPool -Name $site.applicationPool -ErrorAction SilentlyContinue
Start-Website -Name $SiteName -ErrorAction SilentlyContinue

Write-Host "`nHTTPS binding tamamlandi." -ForegroundColor Green
Write-Host "URL        : https://${HostName}:$HttpsPort"
Write-Host "IIS Site   : $SiteName"
Write-Host "Certificate: $($certificate.Thumbprint)"
Write-Host "Firewall   : TCP $HttpsPort inbound acik"
if ($RemoveHttpBinding) {
    Write-Host 'HTTP binding : kaldirildi'
}
