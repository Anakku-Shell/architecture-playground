# Generates the SQL that creates (or updates) the database schema of each version, from its EF Core
# migrations. Use it when the schema must be created WITHOUT starting the application: a DBA reviewing
# the SQL, a production deployment, a database you create by hand. For local development you do not need
# it: each API applies its migrations at startup. Guide: §1.6.
#
# The scripts are idempotent: they record applied migrations in "__EFMigrationsHistory" and skip them,
# so they can run against an empty database or one that is already partly up to date.
#
# Usage (PowerShell, from anywhere):
#   ./scripts/create-schemas.ps1                       # every version that exists
#   ./scripts/create-schemas.ps1 02-clean-hexagonal
# Output: artifacts/sql/<version>.sql (git-ignored). Apply one with, for example:
#   Get-Content artifacts/sql/02-clean-hexagonal.sql | docker compose exec -T postgres psql -U shop -d shop_clean
param([string]$Only = "")

$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

# One entry per schema: output name, project that holds the migrations, DbContext ("" when the project has only one).
# Each new version adds its entry here (04 has one per module, 05 one per service).
$targets = @(
    @{ Name = "01-layered"; Project = "01-layered/src/Shop.Layered.Data"; Context = "" },
    @{ Name = "02-clean-hexagonal"; Project = "02-clean-hexagonal/src/Shop.Clean.Infrastructure"; Context = "" }
)

$out = "artifacts/sql"
New-Item -ItemType Directory -Force $out | Out-Null
dotnet tool restore | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed" }

foreach ($target in $targets) {
    if ($Only -and -not $target.Name.StartsWith($Only)) { continue }
    if (-not (Test-Path $target.Project)) {
        Write-Host "skip  $($target.Name) ($($target.Project) does not exist yet)"
        continue
    }

    $arguments = @("ef", "migrations", "script", "--idempotent", "--project", $target.Project, "--output", "$out/$($target.Name).sql")
    if ($target.Context) { $arguments += @("--context", $target.Context) }
    dotnet @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet ef failed for $($target.Name)" }
    Write-Host "wrote $out/$($target.Name).sql"
}
