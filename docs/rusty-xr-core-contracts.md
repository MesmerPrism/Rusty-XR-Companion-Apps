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

The current bundled catalog includes the Rusty XR Quest composite-layer APK
entry:

- app id: `rusty-xr-quest-composite-layer`
- package: `com.example.rustyxr.composite`
- activity: `.CompositeLayerActivity`
- APK path in releases: `catalogs/apks/rusty-xr-quest-composite-layer-debug.apk`

The APK bytes are not committed. Build the APK from Rusty XR, then use
Companion's catalog install, launch, and verify commands.

The source sample catalog also includes the public Rusty XR Quest broker proof:

- app id: `rusty-xr-quest-broker`
- package: `com.example.rustyxr.broker`
- activity: `.MainActivity`
- source APK path:
  `..\Rusty-XR\examples\quest-broker-apk\build\outputs\rusty-xr-quest-broker-debug.apk`
- profiles: `broker-latency-websocket-lsl` and `broker-osc-drive-ingress`

The broker has been validated with a Unity client on Quest for localhost
WebSocket samples, optional LSL forwarding, and OSC-driven scene values. A
dedicated public Unity example is planned separately.

Recent Rusty XR catalog profiles include native passthrough hotload modes,
`XR_META_passthrough_color_lut` color-LUT flicker modes, and pure full-field
red/black strobe modes that request 120 Hz display refresh. The strobe modes
are intentional high-frequency visual stimuli and require explicit informed
opt-in before launch.

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
