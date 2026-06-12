# Local dev + E2E: Release build, Development DB config, port 8080.
# Usage: pwsh scripts/run-local-e2e.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Stop anything already bound to 8080 (prior dotnet run / docker web container).
$port = 8080
Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue |
    ForEach-Object { $_.OwningProcess } |
    Where-Object { $_ -gt 0 } |
    Sort-Object -Unique |
    ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }

Get-Process -Name METERP.Web -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

dotnet build src/METERP.Web/METERP.Web.csproj -c Release
dotnet run --project src/METERP.Web/METERP.Web.csproj -c Release --launch-profile local-8080