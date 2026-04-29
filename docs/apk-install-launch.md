---
title: APK Install And Launch
nav_order: 4
---

# APK Install And Launch

The companion installs local APKs and launches package targets through ADB.

## Install

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- install --serial <serial> --apk C:\path\app.apk
```

The WPF app provides the same path through **Browse** and **Install**.

## Launch

If the package has a normal launcher activity, package-only launch is enough:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- launch --serial <serial> --package com.example.questapp
```

If you know the exact activity:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- launch --serial <serial> --package com.example.questapp --activity .MainActivity
```

## Stop

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- stop --serial <serial> --package com.example.questapp
```

## Catalogs

Catalogs are public JSON metadata, not a requirement to commit APK bytes. The
example catalog under `samples/quest-session-kit/` uses placeholder package
data so downstream projects can adapt the format safely.

The catalog shape is aligned with Rusty XR core's `quest-app-catalog` schema.
Use `schemaVersion: "rusty.xr.quest-app-catalog.v1"` for new public catalogs.
APK files can still stay as local paths or GitHub Release assets.

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- catalog list --json
```

Install and launch a catalog app by ID:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- catalog install --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-minimal --serial <serial>
dotnet run --project src/RustyXr.Companion.Cli -- catalog launch --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-minimal --serial <serial>
```

Run a launch verification pass with a device profile and a diagnostics bundle:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- catalog verify --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-minimal --serial <serial> --launch --device-profile perf-smoke-test --settle-ms 4000 --out .\artifacts\verify
```

The verifier records headset snapshots plus process, foreground, `gfxinfo`, and
`meminfo` signals. Visual confirmation still requires the headset or a cast
stream.
