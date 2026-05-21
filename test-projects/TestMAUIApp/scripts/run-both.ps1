#Requires -Version 5.1
<#
.SYNOPSIS
  Builds and runs TestMAUIApp on Windows (WinUI) and Android (emulator) together.

.EXAMPLE
  .\scripts\run-both.ps1
  dotnet run --project RunBoth
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [string]$AvdName = 'TestMAUIApp_AVD',
    [string]$AndroidSdkDirectory = $(Join-Path $env:LOCALAPPDATA 'Android\Sdk'),
    [switch]$SkipWindows,
    [switch]$SkipAndroid,
    [switch]$NoBuild,
    [int]$BootTimeoutMinutes = 6
)

$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    $dir = $PSScriptRoot
    while ($dir -and -not (Test-Path (Join-Path $dir 'TestMAUIApp.slnx'))) {
        $parent = Split-Path $dir -Parent
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    if (-not (Test-Path (Join-Path $dir 'TestMAUIApp.slnx'))) {
        throw 'Could not locate repository root (TestMAUIApp.slnx).'
    }
    return $dir
}

function Ensure-AndroidSdk {
    param([string]$SdkRoot)

    New-Item -ItemType Directory -Force -Path $SdkRoot | Out-Null
    $cmdlineLatest = Join-Path $SdkRoot 'cmdline-tools\latest'
    $systemCmdline = 'C:\Program Files (x86)\Android\android-sdk\cmdline-tools\latest'

    if (-not (Test-Path $cmdlineLatest) -and (Test-Path $systemCmdline)) {
        New-Item -ItemType Directory -Force -Path (Join-Path $SdkRoot 'cmdline-tools') | Out-Null
        Copy-Item -Path $systemCmdline -Destination $cmdlineLatest -Recurse -Force
    }

    $sdkmanager = Join-Path $cmdlineLatest 'bin\sdkmanager.bat'
    if (-not (Test-Path $sdkmanager)) {
        throw "Android SDK cmdline-tools not found. Install Android SDK or set -AndroidSdkDirectory."
    }

    1..20 | ForEach-Object { 'y' } | & $sdkmanager --sdk_root=$SdkRoot --licenses 2>&1 | Out-Null

    $packages = @(
        'platform-tools',
        'build-tools;36.0.0',
        'platforms;android-35',
        'platforms;android-36',
        'emulator',
        'system-images;android-35;google_apis;x86_64'
    )
    & $sdkmanager --sdk_root=$SdkRoot @packages 2>&1 | Out-Null
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

function Wait-EmulatorBoot {
    param(
        [string]$Adb,
        [int]$TimeoutMinutes
    )

    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    do {
        Start-Sleep -Seconds 4
        $booted = & $Adb shell getprop sys.boot_completed 2>$null
        if ($booted -match '1') { return }
    } while ((Get-Date) -lt $deadline)

    throw 'Timed out waiting for Android emulator to boot.'
}

function Test-DeviceOnline {
    param([string]$Adb)
    $lines = & $Adb devices 2>&1
    return ($lines -match 'emulator-\d+\s+device' -or $lines -match '\S+\s+device')
}

$repoRoot = Get-RepoRoot
$winProject = Join-Path $repoRoot 'TestMAUIApp.WinUI\TestMAUIApp.WinUI.csproj'
$droidProject = Join-Path $repoRoot 'TestMAUIApp.Droid\TestMAUIApp.Droid.csproj'

Write-Host "Repository: $repoRoot"
Write-Host "Android SDK: $AndroidSdkDirectory"

$env:ANDROID_SDK_ROOT = $AndroidSdkDirectory
$env:ANDROID_HOME = $AndroidSdkDirectory
$env:Path = @(
    (Join-Path $AndroidSdkDirectory 'platform-tools'),
    (Join-Path $AndroidSdkDirectory 'emulator'),
    $env:Path
) -join ';'

if (-not $SkipAndroid) {
    Ensure-AndroidSdk -SdkRoot $AndroidSdkDirectory
    Ensure-Avd -SdkRoot $AndroidSdkDirectory -Name $AvdName

    $adb = Join-Path $AndroidSdkDirectory 'platform-tools\adb.exe'
    if (-not (Test-DeviceOnline -Adb $adb)) {
        Write-Host "Starting emulator '$AvdName'..."
        $emulator = Join-Path $AndroidSdkDirectory 'emulator\emulator.exe'
        Start-Process -FilePath $emulator -ArgumentList @('-avd', $AvdName, '-gpu', 'swiftshader_indirect', '-no-snapshot-load') -WindowStyle Normal | Out-Null
        Wait-EmulatorBoot -Adb $adb -TimeoutMinutes $BootTimeoutMinutes
    }
    else {
        Write-Host 'Android device already online.'
    }

    if (-not $NoBuild) {
        Write-Host 'Installing Android SDK dependencies (API 36)...'
        dotnet build $droidProject `
            -f net10.0-android `
            -t:InstallAndroidDependencies `
            "-p:AndroidSdkDirectory=$AndroidSdkDirectory" `
            "-p:AcceptAndroidSDKLicenses=true" | Out-Host
    }
}

if (-not $SkipWindows) {
    Write-Host 'Starting Windows (WinUI)...'
    $winArgs = @(
        'run',
        '--project', $winProject,
        '-f', 'net10.0-windows10.0.19041.0',
        '-c', $Configuration,
        '-p:Platform=x64'
    )
    if ($NoBuild) { $winArgs += '--no-build' }

    Start-Process -FilePath 'dotnet' -ArgumentList $winArgs -WorkingDirectory $repoRoot -WindowStyle Normal | Out-Null
}

if (-not $SkipAndroid) {
    Write-Host 'Building and deploying Android...'
    $droidArgs = @(
        'build', $droidProject,
        '-f', 'net10.0-android',
        '-c', $Configuration,
        '-t:Run',
        "-p:AndroidSdkDirectory=$AndroidSdkDirectory"
    )
    if ($NoBuild) {
        Write-Warning '-NoBuild applies to Windows only; Android deploy always builds.'
    }

    & dotnet @droidArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # -t:Run already installs and launches the app. Optional: bring launcher to foreground
    # without using `adb monkey` (writes to stderr and PowerShell treats it as an error).
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $adb shell am start -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p com.companyname.testmauiapp 2>$null | Out-Null
    $ErrorActionPreference = $prevEap
}

Write-Host ''
Write-Host 'Done. Windows app launched in a separate process; Android app deployed to the emulator.'
Write-Host 'Stop the emulator from Android Emulator UI or: adb -s emulator-5554 emu kill'
