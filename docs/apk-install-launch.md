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

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- catalog list --json
```
