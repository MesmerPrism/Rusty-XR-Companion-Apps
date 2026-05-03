---
title: APK Install And Launch
nav_order: 4
---

# APK Install And Launch

The companion installs local APKs and launches package targets through ADB.
Published Windows installs also include the public Rusty XR Quest camera
composite-layer and broker APKs. On first launch, the WPF app auto-loads the bundled
catalog from `catalogs/rusty-xr-quest-composite-layer.catalog.json`, selects
the `rusty-xr-quest-composite-layer` app by default, and resolves bundled APKs
under `catalogs/apks/`.

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
published app payload has a default release catalog for the bundled composite
APK. The example catalog under `samples/quest-session-kit/` still uses local
build-output paths so downstream projects can adapt the format safely from
source.

The catalog shape is aligned with Rusty XR core's `quest-app-catalog` schema.
Use `schemaVersion: "rusty.xr.quest-app-catalog.v1"` for new public catalogs.
APK files can be local paths or `https://` GitHub Release asset URLs. Remote
assets are downloaded into `%LOCALAPPDATA%\RustyXrCompanion\apk-cache` before
install. Use `--apk-cache <folder>` to override the cache and
`--refresh-apk-download` to force a fresh download.

For local source builds, keep `Rusty-XR` and `Rusty-XR-Companion-Apps` as
sibling folders. Then run:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- workspace guide
```

The guide reports the expected catalog and APK output paths for the Rusty XR
minimal, composite-layer, and broker examples.

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- catalog list --json
```

Install and launch a catalog app by ID:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- catalog install --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-minimal --serial <serial>
dotnet run --project src/RustyXr.Companion.Cli -- catalog launch --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-minimal --serial <serial>
```

Catalog runtime profile `values` are passed as Activity extras on launch. Use
them for public example toggles such as camera enabled/disabled, requested
camera size, or whether the app should request MediaProjection streaming.
The bundled Rusty XR composite-layer catalog also includes native passthrough
hotload profiles and intentional strobe profiles. Strobing profiles can trigger
seizures or other adverse reactions in people with photosensitive epilepsy or
light-sensitive conditions; launch them only with explicit informed opt-in.
For public background on why safety-gated rhythmic light tools exist as a
design space, see [Brain Candy](https://braincandyapp.com/), a Meta Quest VR
experience that presents its own strobing-lights warning and frames rhythmic
audio-visual stimulation as non-clinical perception exploration.

Use `osc-udp-listener` when validating the generic OSC control/sensor adapter.
It starts a UDP listener on `0.0.0.0:9000`, enables the headset diagnostic HUD,
and keeps camera and MediaProjection sources disabled. Use
`osc-udp-listener-no-overlay` when you want the same listener without drawing
the HUD.

Run a launch verification pass with a device profile and a diagnostics bundle:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- catalog verify --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-minimal --serial <serial> --launch --device-profile perf-smoke-test --settle-ms 4000 --out .\artifacts\verify
```

The verifier records headset snapshots plus process, foreground, `gfxinfo`, and
`meminfo` signals. Visual confirmation still requires the headset or a cast
stream.

For immersive OpenXR examples, a successful launch should also show logcat
signals such as `OpenXR state READY`, `OpenXR state FOCUSED`, swapchain
creation, and recurring frame messages. For camera-driven examples, verify
camera session and frame-upload logs before enabling MediaProjection. If the
app remains at `OpenXR state IDLE`, diagnose OpenXR Activity-context and
Android lifecycle readiness before debugging media streaming.

For the public camera-driven custom-layer example, first validate the camera
renderer without screen streaming:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- catalog verify --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile camera-diagnostic-cpu-copy --settle-ms 7000 --logcat-lines 1000 --out .\artifacts\verify
```

For examples that stream final-screen MediaProjection frames, add the receiver
flags:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- catalog verify --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile media-projection-stream --media-receiver --settle-ms 7000 --logcat-lines 1000 --out .\artifacts\verify
```

The verifier configures `adb reverse`, starts a local receiver, and records how
many media frames arrived. It cannot accept the headset MediaProjection consent
popup for the user.

For OSC probes, launch the listener profile and send a datagram to the Quest LAN
IP. OSC is UDP, so this path does not use `adb reverse`:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- catalog verify --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile osc-udp-listener --settle-ms 5000 --logcat-lines 1000 --out .\artifacts\verify
dotnet run --project src\RustyXr.Companion.Cli -- osc send --host <quest-lan-ip> --port 9000 --address /rusty-xr/probe --arg string:hello
```

The diagnostic HUD can also be toggled on a running compatible APK through a
runtime-profile launch extra such as `rustyxr.diagnosticHudCommand=toggle`.

For the public broker proof-of-concept, use the sibling Rusty XR source catalog
after building `examples\quest-broker-apk`. The `broker-latency-websocket-lsl`
profile starts the localhost HTTP/WebSocket API and forwards samples to LSL
only when the APK was built with a compliant Android `liblsl.so`. The
`broker-osc-drive-ingress` profile listens for `/rusty-xr/drive/radius` on UDP
port `9000` and rebroadcasts accepted values to localhost WebSocket clients:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-broker-apk\catalog\rusty-xr-quest-broker.catalog.json --app rusty-xr-quest-broker --serial <serial> --stop-catalog-apps --install --launch --device-profile broker-smoke-test --runtime-profile broker-osc-drive-ingress --settle-ms 5000 --logcat-lines 1000 --out .\artifacts\verify
dotnet run --project src\RustyXr.Companion.Cli -- osc send --host <quest-lan-ip> --port 9000 --address /rusty-xr/drive/radius --arg float:0.75
```

That OSC ingress path has been validated with
[The Big Red Button Institute](https://github.com/MesmerPrism/the-big-red-button-institute),
the public Unity Quest example for comparing direct Unity OSC/BLE input against
broker-routed stream events.

For passthrough style hotload, launch a passthrough profile once and then send
another catalog profile to the running activity:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- catalog launch --path catalogs\rusty-xr-quest-composite-layer.catalog.json --app rusty-xr-quest-composite-layer --serial <serial> --runtime-profile passthrough-underlay-hotload-neutral
dotnet run --project src\RustyXr.Companion.Cli -- catalog launch --path catalogs\rusty-xr-quest-composite-layer.catalog.json --app rusty-xr-quest-composite-layer --serial <serial> --runtime-profile passthrough-underlay-hotload-lut-opponent
```

The full-field strobe profiles are `full-field-red-black-flicker-10hz`,
`full-field-red-black-flicker-40hz`, and
`full-field-red-black-flicker-60hz`. They request `120 Hz` through the app's
OpenXR refresh-rate path. Treat the request as advisory until logcat confirms
`activeDisplayRefreshHz`, `observedOpenXrFps`, and `full-field flicker stats`.
The passthrough LUT strobe profiles use the same frequency suffixes under
`passthrough-underlay-hotload-lut-flicker-*` and should be treated with the
same warning.

The `camera-gpu-buffer-probe` runtime profile requests the public GPU-buffer
path with CPU fallback disabled. In the current public example that path
imports and samples Camera2 `PRIVATE` hardware buffers, then logs the remaining
projection blockers instead of treating the diagnostic surface as a stereo
projection renderer.

Run `camera-source-diagnostics` before treating a Quest runtime as mono-only.
When `--out` is enabled, the verifier pulls the app-private camera-source
diagnostics payload into `camera-source-diagnostics.json`.
The `camera-stereo-gpu-composite` profile requires logcat to show real paired
left/right GPU buffers, `stereoLayout=Separate`, `activeTier=gpu-projected`,
`alignedProjection=true`, no CPU upload, OpenXR `FOCUSED`, a platform or
public estimated-profile pose source, and a logged `cameraTextureTransform`
with `orientationCheck=true`. The accepted public reference profile also passes
`visualReleaseAccepted=true` with the manual acceptance token after headset
inspection confirmed upright feed, correct source-eye mapping, stable
head-motion projection, and a visible camera-driven border. New device/runtime
variants should rerun diagnostics and keep that manual visual gate closed until
the same conditions are inspected again.

For projection A/B checks, use `camera-stereo-gpu-composite-quad-surface`.
It keeps the same GPU-buffer stereo camera path and visual gate, but passes
`rustyxr.cameraProjectionMode=quad-surface` so the renderer reconstructs the
content-surface UV that a head-anchored quad would rasterize before camera
projection. It also passes
`rustyxr.cameraColorMode=external-rgb` plus a small public
contrast/brightness lift. Compare it with the default
`display-screen-homography` mode when investigating projection geometry,
sampler, tone, or color differences. This profile is intentionally not marked
as the final performance or color reference; keep
`visualReleaseAccepted=false` until a headset/cast inspection confirms the
remaining mode-specific differences are acceptable.

Camera delivery cadence is controlled through ordinary catalog launch extras.
The public Quest composite sample accepts `rustyxr.cameraTargetFps` or
`rustyxr.cameraFpsMin` / `rustyxr.cameraFpsMax` and forwards the nearest
supported value to Camera2 `CONTROL_AE_TARGET_FPS_RANGE`. The companion does
not treat that as a hard frame-rate guarantee; inspect logcat lines beginning
with `Camera2 AE FPS range` and `Camera2 delivery stats` to compare requested,
applied, and observed camera-buffer cadence. The current Quest 3S stereo
Camera2 validation found that `30-30` delivered about `29.85 FPS`, while
`60-60` was accepted by Camera2 but delivered about `49.9 FPS` in the paired
`1280x1280` GPU-buffer profile. This matches Android's
[`CONTROL_AE_TARGET_FPS_RANGE`](https://developer.android.com/reference/android/hardware/camera2/CaptureRequest#CONTROL_AE_TARGET_FPS_RANGE)
warning that actual max frame rate can still be capped by stream
min-frame-duration and runtime constraints.

If the headset shows `Select view you want to share`, select `Entire view` and
press `Share` in the headset. The companion intentionally treats that panel as
manual because current Quest system UI does not reliably accept shell-driven
selection or Share-button taps.

Use `--stop-catalog-apps` when validating immersive examples so another app
from the same catalog cannot keep the headset compositor busy. Use
`--logcat-lines <n>` to save a scoped `logcat.txt` beside `verification.json`
and `verification.md`; logcat is cleared first unless `--keep-logcat` is also
passed.
