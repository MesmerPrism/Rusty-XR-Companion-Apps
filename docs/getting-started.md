---
title: Getting Started
nav_order: 3
---

# Getting Started

## Requirements

- Windows 10 or later
- .NET 10 SDK for source builds
- Quest developer mode enabled
- Android Platform Tools, `adb.exe` on `PATH`, or the companion's managed
  tooling install
- optional: managed `scrcpy` or `scrcpy.exe` on `PATH` for display casting

## Source Build

```powershell
git clone https://github.com/MesmerPrism/Rusty-XR-Companion-Apps.git
cd Rusty-XR-Companion-Apps
dotnet build RustyXr.Companion.slnx
dotnet test RustyXr.Companion.slnx
dotnet run --project src/RustyXr.Companion.Cli -- doctor
dotnet run --project src/RustyXr.Companion.App
```

If Windows policy blocks the raw WPF development output, use the source launcher:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1
```

## First Device Check

1. Put on the Quest and enable developer mode.
2. Connect USB and accept the headset's USB debugging prompt.
3. Run:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- devices
```

4. Install managed tooling if you want Wi-Fi ADB bootstrap, `hzdb`
   proximity/wake controls, and display casting without separate manual tool
   setup:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- tooling install-official
```

5. Capture a snapshot:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- snapshot --serial <serial>
```

6. Optional: enable Wi-Fi ADB from the trusted USB connection:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- wifi enable --serial <usb-serial>
```

## APKs

Published Windows installs include the public Rusty XR Quest camera
composite-layer APK and auto-load its catalog on startup, so the target is
already listed in **Catalog Verify** after setup.

Source builds do not commit APK bytes. Install a local APK file from the WPF
app or CLI:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- install --serial <serial> --apk C:\path\your-app.apk
```

See [Git LFS and assets](lfs-and-assets.md) for why release packaging can
bundle generated public APK assets without committing APK bytes to the repo.
