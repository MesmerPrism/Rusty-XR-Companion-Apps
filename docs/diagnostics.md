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
- optional live headset snapshots
- notes for command failures

The WPF app writes diagnostics under:

```text
%LOCALAPPDATA%\RustyXrCompanion\diagnostics
```

Share the diagnostics folder when filing an issue. Remove anything you consider
private before attaching it publicly.
