# Start METERP locally on http://localhost:8080 with Development config.
# Do NOT use --no-launch-profile (that forces Production + CHANGE_ME DB password).
# Usage: pwsh run-local.ps1
#
# Connection string resolution (no password is stored in this script):
#   1. ConnectionStrings__DefaultConnection already in the environment, or
#   2. gitignored src/METERP.Web/appsettings.Development.local.json, or
#   3. gitignored .env (ConnectionStrings__DefaultConnection=...).
# Otherwise use: dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..." --project src/METERP.Web
# Demo seed stays off for database METERP_Dev. See scripts/run-local-e2e.ps1.

& "$PSScriptRoot\scripts\run-local-e2e.ps1"