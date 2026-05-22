#Requires -Version 5.1
<#
.SYNOPSIS
  Boots the full local dev stack: backend API, WinUI app, Android emulator, and Android app (deploy only if emulator was already running).

.EXAMPLE
  dotnet run --project BootDev
  .\scripts\boot-dev.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [string]$AvdName = 'TestMAUIApp_AVD',
    [string]$AndroidSdkDirectory = $(Join-Path $env:LOCALAPPDATA 'Android\Sdk')
)

$ErrorActionPreference = 'Stop'

function Get-MauiRoot {
    $dir = $PSScriptRoot
    while ($dir -and -not (Test-Path (Join-Path $dir 'TestMAUIApp.slnx'))) {
        $parent = Split-Path $dir -Parent
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    if (-not (Test-Path (Join-Path $dir 'TestMAUIApp.slnx'))) {
        throw 'Could not locate TestMAUIApp.slnx.'
    }
    return $dir
}

function Test-DeviceOnline {
    param([string]$Adb)
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $lines = & $Adb devices 2>&1
    $ErrorActionPreference = $prevEap
    return ($lines -match 'emulator-\d+\s+device\b')
}

function Test-EmulatorReady {
    param([string]$Adb)
    if (-not (Test-DeviceOnline -Adb $Adb)) {
        return $false
    }

    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $booted = & $Adb shell getprop sys.boot_completed 2>&1
    $ErrorActionPreference = $prevEap
    return ($booted -match '1')
}

function Test-BackendRunning {
    try {
        $response = Invoke-WebRequest -Uri 'http://localhost:5000/' -UseBasicParsing -TimeoutSec 2
        return $response.StatusCode -eq 200
    }
    catch {
        return $false
    }
}

function Test-WinUiRunning {
    return $null -ne (Get-Process -Name 'TestMAUIApp.WinUI' -ErrorAction SilentlyContinue)
}

function Ensure-Avd {
    param(
        [string]$SdkRoot,
        [string]$Name
    )

    $emulator = Join-Path $SdkRoot 'emulator\emulator.exe'
    $avdmanager = Join-Path $SdkRoot 'cmdline-tools\latest\bin\avdmanager.bat'
    $avds = & $emulator -list-avds 2>$null

    if ($avds -notcontains $Name) {
        Write-Host "Creating AVD '$Name'..."
        echo no | & $avdmanager create avd -n $Name -k 'system-images;android-35;google_apis;x86_64' -d 'pixel_7' 2>&1 | Out-Null
    }
}

$mauiRoot = Get-MauiRoot
$testProjectsRoot = Split-Path $mauiRoot -Parent
$backendProject = Join-Path $testProjectsRoot 'TestNativeMobileBackendApi\TestNativeMobileBackendApi.csproj'
$winProject = Join-Path $mauiRoot 'TestMAUIApp.WinUI\TestMAUIApp.WinUI.csproj'
$droidProject = Join-Path $mauiRoot 'TestMAUIApp.Droid\TestMAUIApp.Droid.csproj'
$dockerCompose = Join-Path $testProjectsRoot 'TestNativeMobileBackendApi\docker-compose.yml'

Write-Host "MAUI root:      $mauiRoot"
Write-Host "Backend:        $backendProject"
Write-Host "Android SDK:    $AndroidSdkDirectory"
Write-Host ''

# PostgreSQL (optional - API can also start the container on first run)
if (Test-Path $dockerCompose) {
    Write-Host 'Ensuring PostgreSQL container is up...'
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $null = docker compose -f $dockerCompose up -d 2>&1
    $ErrorActionPreference = $prevEap
    Write-Host 'PostgreSQL container ready.'
}

# Backend API
if (Test-BackendRunning) {
    Write-Host 'Backend API already running at http://localhost:5000'
}
else {
    Write-Host 'Starting backend API...'
    Start-Process -FilePath 'dotnet' -ArgumentList @(
        'run',
        '--project', $backendProject,
        '--launch-profile', 'http'
    ) -WorkingDirectory (Split-Path $backendProject -Parent) -WindowStyle Normal | Out-Null
}

# Windows (WinUI)
if (Test-WinUiRunning) {
    Write-Host 'WinUI app already running.'
}
else {
    Write-Host 'Starting Windows (WinUI)...'
    Start-Process -FilePath 'dotnet' -ArgumentList @(
        'run',
        '--project', $winProject,
        '-f', 'net10.0-windows10.0.19041.0',
        '-c', $Configuration,
        '-p:Platform=x64'
    ) -WorkingDirectory $mauiRoot -WindowStyle Normal | Out-Null
}

# Android emulator + conditional deploy
$env:ANDROID_SDK_ROOT = $AndroidSdkDirectory
$env:ANDROID_HOME = $AndroidSdkDirectory
$env:Path = @(
    (Join-Path $AndroidSdkDirectory 'platform-tools'),
    (Join-Path $AndroidSdkDirectory 'emulator'),
    $env:Path
) -join ';'

$adb = Join-Path $AndroidSdkDirectory 'platform-tools\adb.exe'
$emulatorReady = Test-EmulatorReady -Adb $adb

Ensure-Avd -SdkRoot $AndroidSdkDirectory -Name $AvdName

if ($emulatorReady) {
    Write-Host 'Android emulator ready - deploying app...'
    & dotnet build $droidProject `
        -f net10.0-android `
        -c $Configuration `
        -t:Run `
        "-p:AndroidSdkDirectory=$AndroidSdkDirectory"
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'Android deploy failed. Run BootDev again once adb devices shows device.'
    }
}
else {
    if (Test-DeviceOnline -Adb $adb) {
        Write-Host 'Android emulator is online but still booting - app deploy skipped. Run BootDev again shortly.'
    }
    else {
        Write-Host "Starting emulator '$AvdName' (app deploy skipped until emulator is ready - run BootDev again)..."
        $emulatorExe = Join-Path $AndroidSdkDirectory 'emulator\emulator.exe'
        Start-Process -FilePath $emulatorExe -ArgumentList @(
            '-avd', $AvdName,
            '-gpu', 'swiftshader_indirect',
            '-no-snapshot-load'
        ) -WindowStyle Normal | Out-Null
    }
}

Write-Host ''
Write-Host 'Done.'
if (-not $emulatorReady) {
    Write-Host 'Run BootDev again after adb devices shows device to deploy the Android app.'
}
Write-Host 'Demo login: demo / Password1!'
