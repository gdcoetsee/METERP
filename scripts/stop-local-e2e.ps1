# Stop local METERP web app and free port 8080 (pair with run-local-e2e.ps1).
# Usage: pwsh scripts/stop-local-e2e.ps1

$ErrorActionPreference = "Stop"
$port = 8080

Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue |
    ForEach-Object { $_.OwningProcess } |
    Where-Object { $_ -gt 0 } |
    Sort-Object -Unique |
    ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }

Get-Process -Name METERP.Web -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host "Stopped processes on port $port (if any)."