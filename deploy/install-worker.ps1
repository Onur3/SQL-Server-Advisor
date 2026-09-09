param(
    [Parameter(Mandatory=$true)][string]$PublishPath,
    [string]$ServiceName = "SQL Server Advisor Collector"
)

$exe = Join-Path $PublishPath "SqlServerAdvisor.Worker.exe"
if (-not (Test-Path $exe)) { throw "Worker executable not found: $exe" }

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete "$ServiceName" | Out-Null
    Start-Sleep -Seconds 2
}

sc.exe create "$ServiceName" binPath= "`"$exe`"" start= auto | Out-Null
sc.exe failure "$ServiceName" reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
Start-Service -Name $ServiceName
Get-Service -Name $ServiceName
