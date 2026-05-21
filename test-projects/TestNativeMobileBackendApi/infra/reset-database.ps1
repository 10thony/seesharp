# Destroys the local Postgres volume and recreates it from infra/postgres init scripts.
$ErrorActionPreference = "Stop"
$projectRoot = Split-Path $PSScriptRoot -Parent

Push-Location $projectRoot
try {
    docker compose down -v
    docker compose up -d
    Write-Host "Waiting for Postgres..."
    docker compose ps
}
finally {
    Pop-Location
}
