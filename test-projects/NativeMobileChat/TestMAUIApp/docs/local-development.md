# Local development — Windows + Android

Run the MAUI app on **Windows (WinUI)** and an **Android emulator** from this repo.

## Backend API (required for sign-in and chat)

Start **TestNativeMobileBackendAPI** on HTTP port **5000** before using the mobile app:

```powershell
cd ..\TestNativeMobileBackendAPI
dotnet run --launch-profile http
```

The API starts PostgreSQL via Docker when `Docker:Enabled` is true in `appsettings.Development.json`.

| Platform | API base URL configured in app |
|----------|--------------------------------|
| Windows (WinUI) | `http://localhost:5000/` |
| Android emulator | `http://10.0.2.2:5000/` (host machine from the emulator) |

Demo sign-in (seeded user): **demo** / **Password1!**

On a **physical Android device**, change `Constants.AndroidEmulatorApiBaseAddress` in the shared project to `http://<your-pc-lan-ip>:5000/` and ensure the API listens on that interface.

## Quick start (both platforms)

From the repository root:

```powershell
dotnet run --project RunBoth -- --both
```

Shorthand (same behavior — `--both` is optional when using the `RunBoth` project):

```powershell
dotnet run --project RunBoth
```

Or call the script directly:

```powershell
.\scripts\run-both.ps1
```

This will:

1. Ensure the Android SDK under `%LOCALAPPDATA%\Android\Sdk` (emulator, build-tools, API 36, system image)
2. Create the AVD **`TestMAUIApp_AVD`** if it does not exist
3. Start the emulator if no device is online
4. Launch the **Windows** app in a new process
5. **Build, deploy, and launch** the app on the emulator

---

## Prerequisites

| Requirement | Notes |
|-------------|--------|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | `dotnet --version` |
| MAUI workloads | `maui-windows`, `android` (via Visual Studio or `dotnet workload install`) |
| Windows 10/11 | For WinUI target |
| RAM / disk | Emulator + WinUI together need several GB free |

Optional: Android SDK under `C:\Program Files (x86)\Android\android-sdk` — the script copies cmdline-tools from there into your user SDK on first run.

---

## Run one platform only

### Windows only

```powershell
dotnet run --project TestMAUIApp.WinUI\TestMAUIApp.WinUI.csproj `
  -f net10.0-windows10.0.19041.0 -c Debug -p:Platform=x64
```

### Android only

Set the SDK path (same path the script uses):

```powershell
$sdk = "$env:LOCALAPPDATA\Android\Sdk"
$env:ANDROID_SDK_ROOT = $sdk
$env:ANDROID_HOME = $sdk
```

Start the emulator (after first `run-both` created the AVD):

```powershell
& "$sdk\emulator\emulator.exe" -avd TestMAUIApp_AVD
```

Wait until `adb devices` shows `device`, then deploy:

```powershell
dotnet build TestMAUIApp.Droid/TestMAUIApp.Droid.csproj `
  -f net10.0-android -c Debug -t:Run `
  "-p:AndroidSdkDirectory=$sdk"
```

First-time Android SDK setup (if `run-both` has never been run):

```powershell
dotnet build TestMAUIApp.Droid/TestMAUIApp.Droid.csproj `
  -f net10.0-android `
  -t:InstallAndroidDependencies `
  "-p:AndroidSdkDirectory=$sdk" `
  "-p:AcceptAndroidSDKLicenses=true"
```

---

## Script options

`.\scripts\run-both.ps1` supports:

| Parameter | Description |
|-----------|-------------|
| `-Configuration` | `Debug` (default) or `Release` |
| `-AvdName` | Emulator AVD name (default: `TestMAUIApp_AVD`) |
| `-AndroidSdkDirectory` | Override SDK path (default: `%LOCALAPPDATA%\Android\Sdk`) |
| `-SkipWindows` | Only run Android |
| `-SkipAndroid` | Only run Windows |
| `-NoBuild` | Pass `--no-build` to Windows `dotnet run` only |
| `-BootTimeoutMinutes` | Max wait for emulator boot (default: 6) |

Examples:

```powershell
.\scripts\run-both.ps1 -SkipAndroid
.\scripts\run-both.ps1 -SkipWindows
.\scripts\run-both.ps1 -Configuration Release
```

Pass through from the runner:

```powershell
dotnet run --project RunBoth -- --both -SkipWindows
```

---

## Project map

| Project | Target | Role |
|---------|--------|------|
| `TestMAUIApp` | `net10.0` | Shared UI and services |
| `TestMAUIApp.WinUI` | `net10.0-windows10.0.19041.0` | Windows desktop |
| `TestMAUIApp.Droid` | `net10.0-android` | Android |
| `RunBoth` | `net10.0` | Orchestrator (`dotnet run --project RunBoth`) |

---

## Troubleshooting

### `XA0010: No available device`

No emulator is running. Run `.\scripts\run-both.ps1` or start the AVD manually (see Android only above).

### `XA5205: Cannot find aapt2.exe` / `XA5207: android.jar for API level 36`

Point builds at the user SDK and install dependencies:

```powershell
$sdk = "$env:LOCALAPPDATA\Android\Sdk"
dotnet build TestMAUIApp.Droid/TestMAUIApp.Droid.csproj -f net10.0-android `
  -t:InstallAndroidDependencies `
  "-p:AndroidSdkDirectory=$sdk" `
  "-p:AcceptAndroidSDKLicenses=true"
```

Or run `.\scripts\run-both.ps1` once (it installs packages via `sdkmanager`).

### `adb` not recognized

Use the full path or add SDK platform-tools to `PATH`:

```powershell
$sdk = "$env:LOCALAPPDATA\Android\Sdk"
$env:Path = "$sdk\platform-tools;$sdk\emulator;$env:Path"
```

### Windows app already running

`run-both` starts WinUI in a **separate** `dotnet run` process. Close the existing window before starting again, or use `-SkipWindows` when you only need Android.

### Red `adb` / `monkey` text but both apps are running

The deploy step (`dotnet build -t:Run`) already installs and launches the Android app. Older versions of `run-both.ps1` called `adb shell monkey`, which prints informational lines to stderr; PowerShell surfaces those as errors even when the launch succeeded. You can ignore that output if WinUI and the emulator both show the app. The script now uses `am start` with stderr suppressed instead.

### Stop the emulator

```powershell
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" -s emulator-5554 emu kill
```

---

## SQLite / services

The shared library uses SQLite ([MAUI local database docs](https://learn.microsoft.com/en-us/dotnet/maui/data-cloud/database-sqlite?view=net-maui-10.0)). Database file on device/emulator:

`{AppDataDirectory}/TestMAUIApp.db3`

Services are accessed via `MobileAppServices` in the shared project.

---

## Related commands

```powershell
# Build shared library
dotnet build TestMAUIApp/TestMAUIApp.csproj

# Build entire solution (no deploy)
dotnet build TestMAUIApp.slnx
```
