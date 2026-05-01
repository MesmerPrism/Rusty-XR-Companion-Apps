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

The `environment-depth-diagnostics` runtime profile adds OpenXR environment
depth validation. It requires a `Rusty XR environment depth status` log line,
checks that the provider is running, confirms acquired frames and unique
capture timestamps, records observed depth cadence and acquire CPU cost, and
requires the per-eye grayscale depth visualizer draw state. Confidence is
reported explicitly so a run can distinguish available confidence data from a
runtime/API path that exposes no confidence payload.

The WPF app writes diagnostics under:

```text
%LOCALAPPDATA%\RustyXrCompanion\diagnostics
```

Share the diagnostics folder when filing an issue. Remove anything you consider
private before attaching it publicly.
