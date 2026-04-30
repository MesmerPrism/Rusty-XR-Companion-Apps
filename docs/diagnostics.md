---
title: Diagnostics
nav_order: 6
---

# Diagnostics

Diagnostics are designed to be useful on another Windows machine without a
source checkout.

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- doctor --snapshots --out .\artifacts\diagnostics
```

The command writes:

- `diagnostics.json`
- `diagnostics.md`

The report includes:

- Windows and .NET version
- ADB, hzdb, and scrcpy discovery
- ADB device list
- optional live headset snapshots with headset battery, controller batteries, wake state, foreground app, and proximity readback
- notes for command failures

Catalog verification writes a separate bundle:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- catalog verify --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-minimal --serial <serial> --launch --device-profile perf-smoke-test --out .\artifacts\verify
```

That bundle includes before/after snapshots, target process state, foreground
activity matching, `gfxinfo`, `meminfo`, command results, and notes.

The WPF app writes diagnostics under:

```text
%LOCALAPPDATA%\RustyXrCompanion\diagnostics
```

Share the diagnostics folder when filing an issue. Remove anything you consider
private before attaching it publicly.
