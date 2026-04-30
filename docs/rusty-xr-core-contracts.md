---
title: Rusty XR Core Contracts
nav_order: 9
---

# Rusty XR Core Contracts

Rusty XR Companion is the Windows operator shell. The public Rusty XR core owns
the reusable contracts and generated schema shapes.

The current shared connection point is the Quest app catalog:

- schema name: `quest-app-catalog`
- schema version: `rusty.xr.quest-app-catalog.v1`
- Companion fields: `apps`, `deviceProfiles`, and `runtimeProfiles`
- APK files: local paths or public release assets, not committed sample bytes

Future public example APKs should publish catalog metadata in this shape so the
Companion app can install, launch, and profile them without taking a build-time
dependency on the Rust workspace.

The current sample catalog includes the Rusty XR minimal Quest APK entry:

- app id: `rusty-xr-quest-minimal`
- package: `com.example.rustyxr.minimal`
- activity: `.MainActivity`
- APK path: sibling Rusty-XR checkout build output, if built locally

The APK bytes are not committed. Build the APK from Rusty XR, then use
Companion's catalog install, launch, and verify commands.

For a local source workspace, keep both repos as siblings:

```text
<workspace>\Rusty-XR
<workspace>\Rusty-XR-Companion-Apps
```

Then run the companion guide:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- workspace guide --root <workspace>
```

When the Rusty XR core repo is checked out next to this repo, validate this
sample catalog with:

```powershell
python ..\Rusty-XR\tools\schema\check_quest_app_catalog.py samples\quest-session-kit\apk-catalog.example.json
```
