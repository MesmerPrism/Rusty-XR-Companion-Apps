---
title: Source Workspace
nav_order: 4.5
---

# Source Workspace

Use this path when a developer or local agent has both public repos installed
and wants to build Rusty XR example APKs from source, then install, launch, and
verify them through Rusty XR Companion.

## Recommended Layout

Keep the repositories as siblings under one workspace folder:

```text
<workspace>\Rusty-XR
<workspace>\Rusty-XR-Companion-Apps
```

That layout keeps catalog paths short and lets the companion sample catalog
resolve Rusty XR ignored build outputs without machine-specific paths.

Run the source-workspace guide from the companion repo:

```powershell
dotnet run --project .\src\RustyXr.Companion.Cli -- workspace guide --root <workspace>
```

The same command is available from a published CLI zip as:

```powershell
.\RustyXr.Companion.Cli.exe workspace guide --root <workspace>
```

Published app and CLI zips also include `agent-onboarding\AGENTS.md` and this
document as `agent-onboarding\source-workspace.md`, so a local agent can start
from a release install before the source repos exist.

Use `--json` when an agent needs structured paths and commands instead of the
Markdown guide.

## What Companion Provides

The release app and CLI manage the operator-side pieces that can reasonably be
shipped or downloaded into a per-user cache:

- Android platform-tools / `adb`
- Meta `hzdb`
- `scrcpy` for display casting
- catalog APK downloads from public release assets
- diagnostics, screenshot, logcat, and media-receiver bundles

The companion does not ship the Rust compiler, Android SDK/NDK/JDK, OpenXR
loader binaries, signing identity, or app-specific release payloads. Those are
large, license-sensitive, or machine-specific build inputs. Install them only
on machines that need to build APKs from source.

## Source APK Prerequisites

- .NET 10 SDK for companion source builds.
- Rust / Cargo with `aarch64-linux-android` for Rusty XR APK examples.
- Android SDK, NDK, build tools, and JDK layout accepted by the Rusty XR example
  build scripts. The current scripts accept an Android-player-style root with
  `SDK`, `NDK`, and `OpenJDK` children.
- Quest-compatible OpenXR loader only for the immersive composite-layer
  example.

The companion CLI can install and launch APKs without Android Studio. Android
build tooling is required only when building new APK bytes from source.

## Agent Bootstrap

From `Rusty-XR-Companion-Apps`:

```powershell
git status --short
dotnet build RustyXr.Companion.slnx
dotnet run --project .\src\RustyXr.Companion.Cli -- workspace guide
dotnet run --project .\src\RustyXr.Companion.Cli -- tooling install-official
dotnet run --project .\src\RustyXr.Companion.Cli -- devices
```

From `Rusty-XR`:

```powershell
git status --short
cargo test --workspace
powershell -ExecutionPolicy Bypass -File .\examples\quest-minimal-apk\tools\Build-QuestMinimalApk.ps1
```

Then return to `Rusty-XR-Companion-Apps` and verify the built APK:

```powershell
dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-minimal-apk\catalog\rusty-xr-quest-minimal.catalog.json --app rusty-xr-quest-minimal --serial <serial> --install --launch --device-profile perf-smoke-test --runtime-profile minimal-contract-log --settle-ms 4000 --out .\artifacts\verify
```

For the immersive example, build in `Rusty-XR` with an OpenXR loader:

```powershell
powershell -ExecutionPolicy Bypass -File .\examples\quest-composite-layer-apk\tools\Build-QuestCompositeLayerApk.ps1 -OpenXrLoaderPath <path-to-libopenxr_loader.so>
```

Verify from `Rusty-XR-Companion-Apps`:

```powershell
dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-composite-layer-apk\catalog\rusty-xr-quest-composite-layer.catalog.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile camera-stereo-gpu-composite --settle-ms 9000 --logcat-lines 1400 --out .\artifacts\verify
```

## Working Rules

Keep generated files in ignored output locations:

- Rusty XR APK outputs stay under example `build\` folders.
- Companion verification bundles stay under `artifacts\`.
- APK bytes, signing material, screenshots, logcat dumps, media frames, and
  local caches stay out of git.

When a catalog path breaks, first confirm the sibling repo layout, then run
`workspace guide --json` and compare the reported catalog and APK paths.
