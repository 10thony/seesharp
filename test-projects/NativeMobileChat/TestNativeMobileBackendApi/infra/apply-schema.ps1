# Re-apply infra SQL to an existing local Postgres container (idempotent scripts).
param(
    [string]$ContainerName = "testnativemobile-postgres",
    [string]$BootstrapUser = "chatapp",
    [string]$Database = "chatdb"
)

$ErrorActionPreference = "Stop"
$root = Join-Path $PSScriptRoot "postgres"
$scripts = @(
    "01-extensions.sql",
    "02-database-roles.sql",
    "03-schema.sql",
    "04-seed-data.sql"
)

foreach ($script in $scripts) {
    $path = Join-Path $root $script
    Write-Host "Applying $script ..."
    Get-Content $path -Raw | docker exec -i $ContainerName psql -v ON_ERROR_STOP=1 -U $BootstrapUser -d $Database
}

Write-Host "Done."
