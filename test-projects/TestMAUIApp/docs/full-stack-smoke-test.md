# Full-stack smoke test runbook

Boot **TestNativeMobileBackendApi** (Postgres + API) and **TestMAUIApp** (WinUI + Android emulator) for a quick end-to-end chat check.

Use this when you need to redo the temp status check without hiccups. For MAUI-only details, see [local-development.md](./local-development.md).

---

## What gets started

| Component | Location | Port / target |
|-----------|----------|---------------|
| PostgreSQL (Docker) | `test-projects/TestNativeMobileBackendApi/docker-compose.yml` | `localhost:5432` |
| Backend API | `test-projects/TestNativeMobileBackendApi` | `http://localhost:5000` |
| MAUI WinUI app | `test-projects/TestMAUIApp/TestMAUIApp.WinUI` | Desktop window |
| MAUI Android app | `test-projects/TestMAUIApp/TestMAUIApp.Droid` | `TestMAUIApp_AVD` emulator |

Demo sign-in (seeded user): **demo** / **Password1!**

---

## Prerequisites (one-time)

From any shell:

```powershell
dotnet --version          # expect 10.x
docker version            # Docker Desktop running
dotnet workload list      # expect maui-windows and android
```

Android SDK defaults to `%LOCALAPPDATA%\Android\Sdk`. First run of `run-both.ps1` creates the AVD and installs SDK packages.

---

## Recommended order

Start components in this order. Each step includes a quick health check.

### 1. PostgreSQL (Docker)

From the backend project folder:

```powershell
cd C:\projects\jmartinez\seesharp\test-projects\TestNativeMobileBackendApi
docker compose up -d
```

**Check:**

```powershell
docker ps --filter name=testnativemobile-postgres --format "{{.Names}} {{.Status}}"
# expect: testnativemobile-postgres Up ... (healthy)
```

You can skip this step if the API is configured with `"Docker": { "Enabled": true }` in `appsettings.Development.json` — the API will start the container on launch. Starting Docker manually first avoids waiting during API startup.

Schema and seed data live in `infra/postgres/` and run on first container init.

### 2. Backend API

Keep this running in its own terminal:

```powershell
cd C:\projects\jmartinez\seesharp\test-projects\TestNativeMobileBackendApi
dotnet run --launch-profile http
```

**Check:**

```powershell
(Invoke-WebRequest http://localhost:5000/ -UseBasicParsing).StatusCode
# expect: 200
```

Leave this terminal open. The web UI and SignalR hub are served from the same host.

### 3. MAUI apps (Windows + Android)

Open a **second** terminal:

```powershell
cd C:\projects\jmartinez\seesharp\test-projects\TestMAUIApp
dotnet run --project RunBoth -- --both
```

Or call the script directly:

```powershell
cd C:\projects\jmartinez\seesharp\test-projects\TestMAUIApp
.\scripts\run-both.ps1
```

This will:

1. Ensure Android SDK packages and the `TestMAUIApp_AVD` emulator exist
2. Start the emulator if none is online
3. Launch WinUI in a separate process
4. Build, deploy, and run the Android app

**Check — Windows:** a "Test Chat" window opens showing `API: http://localhost:5000/`.

**Check — Android:**

```powershell
$sdk = "$env:LOCALAPPDATA\Android\Sdk"
& "$sdk\platform-tools\adb.exe" devices
# expect: emulator-5554    device

& "$sdk\platform-tools\adb.exe" shell dumpsys activity activities |
  Select-String topResumedActivity
# expect: com.companyname.testmauiapp/...MainActivity
```

Sign in on both clients with **demo** / **Password1!** and send a message to confirm chat + SignalR.

---

## Manual path (when RunBoth misbehaves)

Use this if the orchestrator fails but you still want both clients up.

### Set Android env (every new shell)

```powershell
$sdk = "$env:LOCALAPPDATA\Android\Sdk"
$env:ANDROID_SDK_ROOT = $sdk
$env:ANDROID_HOME = $sdk
$env:Path = "$sdk\platform-tools;$sdk\emulator;$env:Path"
```

### Start emulator

```powershell
& "$sdk\emulator\emulator.exe" -avd TestMAUIApp_AVD -gpu swiftshader_indirect -no-snapshot-load
```

Wait until boot completes:

```powershell
& "$sdk\platform-tools\adb.exe" shell getprop sys.boot_completed
# expect: 1
```

### Windows app (separate terminal)

```powershell
cd C:\projects\jmartinez\seesharp\test-projects\TestMAUIApp
dotnet run --project TestMAUIApp.WinUI\TestMAUIApp.WinUI.csproj `
  -f net10.0-windows10.0.19041.0 -c Debug -p:Platform=x64
```

### Android deploy

```powershell
cd C:\projects\jmartinez\seesharp\test-projects\TestMAUIApp
dotnet build TestMAUIApp.Droid/TestMAUIApp.Droid.csproj `
  -f net10.0-android -c Debug -t:Run `
  "-p:AndroidSdkDirectory=$sdk"
```

If the app is installed but not visible, bring it to the foreground:

```powershell
& "$sdk\platform-tools\adb.exe" shell am start `
  -a android.intent.action.MAIN `
  -c android.intent.category.LAUNCHER `
  -n com.companyname.testmauiapp/crc64cedd2a54a374893b.MainActivity
```

The activity hash (`crc64...`) can change after rebuilds. To look up the current value:

```powershell
& "$sdk\platform-tools\adb.exe" shell dumpsys package com.companyname.testmauiapp |
  Select-String "android.intent.action.MAIN" -Context 0,1
```

---

## Troubleshooting

### `run-both.ps1` exits immediately with red `adb` text

`adb` writes `"daemon starting..."` to stderr on first use. The script suppresses this; if you see the failure on an older copy, pull latest `scripts/run-both.ps1` or use the manual path above.

### Android app opens then closes instantly

Usually a **stale cached auth token** from a previous session. Logcat shows:

```
HttpRequestException: 401 (Unauthorized)
  at MainPage.OnAppearing → SignalR negotiate
```

**Quick fix — clear app data:**

```powershell
$sdk = "$env:LOCALAPPDATA\Android\Sdk"
& "$sdk\platform-tools\adb.exe" shell pm clear com.companyname.testmauiapp
```

Then redeploy with `dotnet build ... -t:Run`. The app now handles expired sessions gracefully and prompts you to sign in again.

### `XA0010: No available device`

Emulator is not running or not booted. Run step 3 (emulator) from the manual path and confirm `adb devices` shows `device`, not `offline`.

### API returns errors / chat empty

Confirm Postgres is healthy and the API terminal shows no connection errors:

```powershell
docker ps --filter name=testnativemobile-postgres
Invoke-WebRequest http://localhost:5000/ -UseBasicParsing
```

To reset the database:

```powershell
cd C:\projects\jmartinez\seesharp\test-projects\TestNativeMobileBackendApi
.\infra\reset-database.ps1
```

Then restart the API.

### WinUI build seems stuck

First WinUI build after a clean can take several minutes with little console output. Wait for the window to appear, or run the Windows-only command in its own terminal to see full build output.

---

## Shut down

```powershell
# Stop emulator
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" -s emulator-5554 emu kill

# Stop API: Ctrl+C in its terminal

# Stop Postgres (optional — container is safe to leave running)
cd C:\projects\jmartinez\seesharp\test-projects\TestNativeMobileBackendApi
docker compose down
```

---

## API URLs by platform

| Client | Base URL |
|--------|----------|
| WinUI | `http://localhost:5000/` |
| Android emulator | `http://10.0.2.2:5000/` |
| Physical Android device | `http://<your-pc-lan-ip>:5000/` (update `Constants.AndroidEmulatorApiBaseAddress`) |

---

## Copy-paste checklist

```
[ ] dotnet run --project BootDev   (from test-projects/TestMAUIApp)
[ ] docker ps → testnativemobile-postgres healthy (optional if API starts Docker)
[ ] curl/Invoke-WebRequest http://localhost:5000/ → 200
[ ] adb devices → emulator-5554 device
[ ] WinUI sign-in window open
[ ] Android app on emulator (second BootDev run if emulator was cold-started)
[ ] Sign in demo / Password1! on both
[ ] Send a message, see it on the other client
```
