# Runs SeeSharp multi-project harness and logs output.
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$LogDir = "",
    [switch]$MultiProject
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($LogDir)) {
    $LogDir = Join-Path $RepoRoot "artifacts\harness-runs"
}
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$logFile = Join-Path $LogDir "harness_$stamp.log"

$env:DOTNET_ENVIRONMENT = "Development"
$env:SEESHARP_DISABLE_WATCH = "1"
$env:SEESHARP_STREAM_MAIN_MODEL = "0"
if ($MultiProject) {
    $env:SEESHARP_MULTI_PROJECT = "1"
}

Push-Location $RepoRoot
try {
    Write-Host "[Harness] Logging to $logFile"
    $args = @("run", "--project", "SeeSharp.csproj", "--no-build")
    if ($MultiProject) { $args += @("--", "--multi-project") }
    & dotnet @args 2>&1 | Tee-Object -FilePath $logFile
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
