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
APK files can be local paths or `https://` GitHub Release asset URLs. Remote
assets are downloaded into `%LOCALAPPDATA%\RustyXrCompanion\apk-cache` before
install. Use `--apk-cache <folder>` to override the cache and
`--refresh-apk-download` to force a fresh download.

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

If the headset shows `Select view you want to share`, select `Entire view` and
press `Share` in the headset. The companion intentionally treats that panel as
manual because current Quest system UI does not reliably accept shell-driven
selection or Share-button taps.

Use `--stop-catalog-apps` when validating immersive examples so another app
from the same catalog cannot keep the headset compositor busy. Use
`--logcat-lines <n>` to save a scoped `logcat.txt` beside `verification.json`
and `verification.md`; logcat is cleared first unless `--keep-logcat` is also
passed.
