# Local dev + E2E: Release build, Development DB config, port 8080.
# Usage: pwsh scripts/run-local-e2e.ps1
#
# Reads ConnectionStrings__DefaultConnection from the environment or a gitignored local file.
# Does not embed a database password. appsettings.Development.json is Password=CHANGE_ME only.
# Sets METERP_SEED_DEMO=true only when that connection string names a database other than METERP_Dev.
# CI sets METERP_SEED_DEMO=true itself for the compose demo database.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Resolve-MeterpLocalConnection {
    if (-not [string]::IsNullOrWhiteSpace($env:ConnectionStrings__DefaultConnection)) {
        Write-Host "Using ConnectionStrings__DefaultConnection from the environment."
        return
    }

    $candidates = @(
        (Join-Path $root "src/METERP.Web/appsettings.Development.local.json"),
        (Join-Path $root "appsettings.Development.local.json"),
        (Join-Path $root ".env")
    )

    foreach ($path in $candidates) {
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $raw = Get-Content -LiteralPath $path -Raw
        $value = $null
        if ($path.EndsWith(".json")) {
            if ($raw -match '"DefaultConnection"\s*:\s*"((?:\\.|[^"\\])*)"') {
                $value = [System.Text.RegularExpressions.Regex]::Unescape($Matches[1])
            }
        }
        else {
            foreach ($line in ($raw -split "`r?`n")) {
                if ($line -match '^\s*#' -or [string]::IsNullOrWhiteSpace($line)) { continue }
                if ($line -match '^\s*ConnectionStrings__DefaultConnection\s*=\s*(.*)$') {
                    $value = $Matches[1].Trim().Trim('"').Trim("'")
                    break
                }
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $env:ConnectionStrings__DefaultConnection = $value
            Write-Host "Loaded ConnectionStrings__DefaultConnection from $(Split-Path -Leaf $path)."
            return
        }
    }

    Write-Host "No ConnectionStrings__DefaultConnection in the environment or a gitignored local file. Set the env var or run: dotnet user-secrets set `"ConnectionStrings:DefaultConnection`" `"...`" --project src/METERP.Web"
}

Resolve-MeterpLocalConnection

if ([string]::IsNullOrWhiteSpace($env:METERP_SEED_DEMO)) {
    $dbName = $null
    if ($env:ConnectionStrings__DefaultConnection -match '(?:Database|Initial Catalog)\s*=\s*([^;]+)') {
        $dbName = $Matches[1].Trim()
    }

    if ($dbName -and $dbName -ne "METERP_Dev") {
        $env:METERP_SEED_DEMO = "true"
        Write-Host "METERP_SEED_DEMO=true for database $dbName."
    }
    else {
        Write-Host "METERP_SEED_DEMO left off (database is METERP_Dev or not set in this script). Enable it only for a separate CI/demo database."
    }
}

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