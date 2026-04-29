---
title: Getting Started
nav_order: 2
---

# Getting Started

## Requirements

- Windows 10 or later
- .NET 10 SDK for source builds
- Quest developer mode enabled
- Android Platform Tools, or `adb.exe` on `PATH`
- optional: `scrcpy.exe` on `PATH` for display casting

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

4. Capture a snapshot:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- snapshot --serial <serial>
```

## No Bundled APK

This first release does not bundle an APK. Install a local APK file from the
WPF app or CLI:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- install --serial <serial> --apk C:\path\your-app.apk
```

See [Git LFS and assets](lfs-and-assets.md) for why this repo includes LFS
patterns but avoids committing APK bytes by default.
