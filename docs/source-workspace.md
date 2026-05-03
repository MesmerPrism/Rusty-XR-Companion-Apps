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
- Optional compliant Android `liblsl.so` only for LSL-capable broker APK
  builds.

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

For OpenXR environment-depth diagnostics, use the same APK with the explicit
depth runtime profile:

```powershell
dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-composite-layer-apk\catalog\rusty-xr-quest-composite-layer.catalog.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile environment-depth-diagnostics --settle-ms 9000 --logcat-lines 1400 --out .\artifacts\verify
```

That profile validates provider support, swapchain metadata, acquisition
cadence, runtime depth timestamps, acquire CPU cost, confidence availability,
and the stereo grayscale depth visualizer state.

For a generic OSC adapter smoke test, launch the listener profile and send a
probe to the Quest's LAN IP. Use `osc-udp-listener-no-overlay` instead when
you want the same UDP listener without drawing the headset diagnostic HUD:

```powershell
dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-composite-layer-apk\catalog\rusty-xr-quest-composite-layer.catalog.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile osc-udp-listener --settle-ms 5000 --logcat-lines 1000 --out .\artifacts\verify
dotnet run --project .\src\RustyXr.Companion.Cli -- osc send --host <quest-lan-ip> --port 9000 --address /rusty-xr/probe --arg string:hello
```

For the broker proof-of-concept, build the sidecar APK in `Rusty-XR`:

```powershell
powershell -ExecutionPolicy Bypass -File .\examples\quest-broker-apk\tools\Build-QuestBrokerApk.ps1
```

Pass `-LslAndroidLibraryPath <path-to-android-arm64-liblsl.so>` only when you
are intentionally packaging a compliant native LSL build. Without it, the APK
still answers status requests, accepts localhost WebSocket latency samples,
logs diagnostics, and supports OSC ingress/egress.

Verify the broker launch from `Rusty-XR-Companion-Apps`:

```powershell
dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-broker-apk\catalog\rusty-xr-quest-broker.catalog.json --app rusty-xr-quest-broker --serial <serial> --stop-catalog-apps --install --launch --device-profile broker-smoke-test --runtime-profile broker-latency-websocket-lsl --settle-ms 5000 --logcat-lines 1000 --out .\artifacts\verify
```

Forward the broker's device-local TCP endpoint to the operator machine and
probe the general broker contract:

```powershell
dotnet run --project .\src\RustyXr.Companion.Cli -- broker forward --serial <serial>
dotnet run --project .\src\RustyXr.Companion.Cli -- broker status --json
dotnet run --project .\src\RustyXr.Companion.Cli -- broker capabilities --json
dotnet run --project .\src\RustyXr.Companion.Cli -- broker streams --json
dotnet run --project .\src\RustyXr.Companion.Cli -- broker sample --subscribe --json
```

For broker app-context Camera2 payload probing, first make sure the public
broker example has runtime camera permission on the selected Quest. The
diagnostic below opens a bounded `YUV_420_888` capture inside the broker APK,
copies luma only, forwards a binary TCP side channel, and validates the
received `raw_luma8` packets on the host. Saved payloads can be inspected for
frame alignment, hashes, per-frame luma checksums/statistics, and optional PGM
contact sheets. This is a payload-transport probe, not an encoded video,
decode-to-texture, or OpenXR layer provider.

```powershell
adb shell pm grant com.example.rustyxr.broker android.permission.CAMERA
adb shell pm grant com.example.rustyxr.broker horizonos.permission.HEADSET_CAMERA
dotnet run --project .\src\RustyXr.Companion.Cli -- broker app-camera-luma-probe --serial <serial> --camera-id <id> --frame-count 2 --payload-out .\artifacts\broker-app-camera\luma.raw --json
dotnet run --project .\src\RustyXr.Companion.Cli -- media inspect-raw-luma --payload .\artifacts\broker-app-camera\luma.raw --width 720 --height 480 --contact-sheet .\artifacts\broker-app-camera\luma.pgm --json
dotnet run --project .\src\RustyXr.Companion.Cli -- broker status --json
```

For a bounded encoded version of the same broker app-context camera lane, use
the H.264 probe. It routes Camera2 frames directly into Android's platform
MediaCodec encoder input surface, receives the encoded packets over the same
binary framing, and reports the H.264 Annex-B/NAL summary. This still stops at
payload transport: the active XR client/provider owns decode-to-texture and
OpenXR submission.

```powershell
dotnet run --project .\src\RustyXr.Companion.Cli -- broker app-camera-h264-probe --serial <serial> --camera-id <id> --capture-ms 900 --max-packets 12 --payload-out .\artifacts\broker-app-camera\camera.h264 --json
dotnet run --project .\src\RustyXr.Companion.Cli -- media inspect-h264 --payload .\artifacts\broker-app-camera\camera.h264 --json
dotnet run --project .\src\RustyXr.Companion.Cli -- broker app-camera-h264-decode-probe --serial <serial> --camera-id <id> --capture-ms 900 --max-packets 12 --json
dotnet run --project .\src\RustyXr.Companion.Cli -- broker status --json
```

After the broker-local decode probe succeeds, the composite-layer APK can run
a separate cross-app consumer probe. Launch the composite example with its own
camera path disabled and `rustyxr.brokerH264Consumer=true`; the app sends the
broker start command over device-local WebSocket, connects to the broker's
device-local H.264 binary stream, and decodes the packets with Android
MediaCodec. The default output mode is `surface-texture`, which renders into a
Java-owned external OES texture through `SurfaceTexture`; pass
`rustyxr.brokerH264DecodeOutputMode=byte-buffer` for the earlier byte-buffer
fixture. Success appears in logcat as `Rusty XR broker H.264 consumer probe`
with `decode_succeeded=true` and, in surface mode,
`surface_texture_update_count` greater than zero. This is still a consumption
fixture, not decode-to-Vulkan texture submission.

```powershell
adb shell am start -a android.intent.action.MAIN -c com.oculus.intent.category.VR -n com.example.rustyxr.composite/.CompositeLayerActivity --ez rustyxr.camera false --ez rustyxr.mediaProjection false --ez rustyxr.brokerH264Consumer true --es rustyxr.brokerH264CameraId <id> --ei rustyxr.brokerH264Width 720 --ei rustyxr.brokerH264Height 480 --ei rustyxr.brokerH264CaptureMs 900 --ei rustyxr.brokerH264MaxPackets 12 --es rustyxr.brokerH264DecodeOutputMode surface-texture
```

To push the same broker stream through the OpenXR layer path, use
`rustyxr.brokerH264DecodeOutputMode=hardware-buffer` and
`rustyxr.cameraTier=gpu-buffer-probe` while keeping `rustyxr.camera=false`.
That decodes into `ImageReader` `PRIVATE` hardware buffers, feeds them through
the native `AHardwareBuffer` bridge, and draws them with the existing Vulkan
GPU-buffer-probe renderer. Expected logcat evidence includes
`hardware_buffer_native_accepted_count` greater than zero and
`Rusty XR GPU-sampled diagnostic camera surface`. When the broker stream-start
result includes selected Camera2 metadata, the composite report should also log
`broker_projection_metadata_attached=true` and
`broker_projection_metadata_ready=true`; native logs should show
`intrinsics=available`, `pose=available`, `poseSource=platform`, and
`projection metadata is available`.

```powershell
adb shell am start -a android.intent.action.MAIN -c com.oculus.intent.category.VR -n com.example.rustyxr.composite/.CompositeLayerActivity --ez rustyxr.camera false --es rustyxr.cameraTier gpu-buffer-probe --ez rustyxr.cameraAllowCpuFallback false --ei rustyxr.cameraCpuUploadHz 0 --ez rustyxr.mediaProjection false --ez rustyxr.brokerH264Consumer true --es rustyxr.brokerH264CameraId <id> --ei rustyxr.brokerH264Width 720 --ei rustyxr.brokerH264Height 480 --ei rustyxr.brokerH264CaptureMs 900 --ei rustyxr.brokerH264MaxPackets 12 --es rustyxr.brokerH264DecodeOutputMode hardware-buffer
```

To find the stereo boundary, enable `rustyxr.brokerH264Stereo=true`, provide
device-specific left/right camera IDs, and use the `gpu-projected` tier with
explicit texture-transform provenance. The consumer starts two bounded broker
streams, decodes both through Android MediaCodec, pairs decoded hardware
buffers by index, and records per-eye resolution, packet rate, decoded frame
rate, payload bitrate, stereo pair acceptance, and timestamp deltas. Projection
is only claimed if logcat also shows `Rusty XR final projection status` with
`alignedProjection=true`.

```powershell
adb shell am start -a android.intent.action.MAIN -c com.oculus.intent.category.VR -n com.example.rustyxr.composite/.CompositeLayerActivity --ez rustyxr.camera false --es rustyxr.cameraTier gpu-projected --es rustyxr.cameraStereoLayout separate --ez rustyxr.cameraAllowCpuFallback false --ei rustyxr.cameraCpuUploadHz 0 --ez rustyxr.mediaProjection false --ez rustyxr.brokerH264Consumer true --ez rustyxr.brokerH264Stereo true --es rustyxr.brokerH264LeftCameraId <left-id> --es rustyxr.brokerH264RightCameraId <right-id> --ei rustyxr.brokerH264StreamPort 8879 --ei rustyxr.brokerH264RightStreamPort 8880 --ei rustyxr.brokerH264Width 720 --ei rustyxr.brokerH264Height 480 --ei rustyxr.brokerH264CaptureMs 900 --ei rustyxr.brokerH264MaxPackets 12 --es rustyxr.brokerH264DecodeOutputMode hardware-buffer --es rustyxr.cameraTextureTransformSource public-broker-h264-stereo-visual-check --es rustyxr.cameraTextureTransformReason visual-check --es rustyxr.leftCameraTextureTransformSource public-broker-h264-left-visual-check --es rustyxr.leftCameraTextureTransformReason visual-check --es rustyxr.rightCameraTextureTransformSource public-broker-h264-right-visual-check --es rustyxr.rightCameraTextureTransformReason visual-check --ez rustyxr.visualReleaseAccepted false
```

For provider-cadence work, add `rustyxr.brokerH264LiveStream=true` and use a
larger bounded packet window. The broker accepts both binary stream sockets
before Camera2 starts, writes schema-2 source timestamps while draining encoder
output, and the composite app receives left/right streams concurrently. Check
the compact stereo summary for source packet rate, wire packet rate, decoded
frame rate, and native pair acceptance.

```powershell
adb shell am start -a android.intent.action.MAIN -c com.oculus.intent.category.VR -n com.example.rustyxr.composite/.CompositeLayerActivity --ez rustyxr.camera false --es rustyxr.cameraTier gpu-projected --es rustyxr.cameraStereoLayout separate --ez rustyxr.cameraAllowCpuFallback false --ei rustyxr.cameraCpuUploadHz 0 --ez rustyxr.mediaProjection false --ez rustyxr.brokerH264Consumer true --ez rustyxr.brokerH264Stereo true --ez rustyxr.brokerH264LiveStream true --es rustyxr.brokerH264LeftCameraId <left-id> --es rustyxr.brokerH264RightCameraId <right-id> --ei rustyxr.brokerH264StreamPort 8879 --ei rustyxr.brokerH264RightStreamPort 8880 --ei rustyxr.brokerH264Width 720 --ei rustyxr.brokerH264Height 480 --ei rustyxr.brokerH264CaptureMs 15000 --ei rustyxr.brokerH264MaxPackets 120 --ei rustyxr.brokerH264BitrateBps 2000000 --ei rustyxr.brokerH264StreamTimeoutMs 30000 --ei rustyxr.brokerH264DecodeTimeoutMs 20000 --es rustyxr.brokerH264DecodeOutputMode hardware-buffer --es rustyxr.cameraTextureTransformSource public-broker-h264-live-stereo-visual-check --es rustyxr.cameraTextureTransformReason visual-check --es rustyxr.leftCameraTextureTransformSource public-broker-h264-live-left-visual-check --es rustyxr.leftCameraTextureTransformReason visual-check --es rustyxr.rightCameraTextureTransformSource public-broker-h264-live-right-visual-check --es rustyxr.rightCameraTextureTransformReason visual-check --ez rustyxr.visualReleaseAccepted false
```

Build and launch the optional ADB shell helper from the same source workspace
after the broker APK is running. The helper is pushed to `/data/local/tmp`,
launched by ADB with `app_process`, reports its UID/capabilities into the
broker, and then exits. It runs as Android `shell` only because the authorized
ADB host starts it; the installed broker APK does not gain that identity. Add
`--probe-codecs` when you want a bounded MediaCodec H.264/H.265/AV1 capability
summary in broker status before starting encoded-video work. Add
`--probe-cameras` when you want a bounded shell-visible camera metadata probe
from `dumpsys media.camera`; the helper parses camera counts, API1 mappings,
per-device lens pose/intrinsics, FPS rows, and stream-configuration rows into
broker status without copying raw dumpsys text. Add `--probe-camera-open` to
attempt a bounded shell-context Camera2 open plus a tiny YUV_420_888 one-frame
capture; the broker maps the metadata/open/capture result into
`cameraProvider` and `projectionProfile` status for source selection. Add
`--emit-synthetic-video-metadata` to have the helper register a metadata-only
synthetic H.264 stream and a bounded set of encoded-sample metadata events; no
frame bytes are sent through JSON. Use `binary-probe` when you want the CLI to
also create the ADB TCP forward, launch the helper, receive the bounded
synthetic binary stream, and validate its framing/checksums on the host. Add
`--mediacodec-synthetic` to make the helper encode a tiny synthetic Surface
source through Android MediaCodec and verify the resulting variable-size H.264
packets over the same side channel. That path also records one broker
video-lab metric sample for helper encode/write timing and drop/stale/queue
counters, while the host stream report includes connection attempts, connect,
read, and total receive durations, and total wire bytes. Add
`--screenrecord-source` to run the shell-only Android `screenrecord` display
capture path and validate its H.264 stdout chunks over the same binary framing.
Binary probe stream reports include a lightweight H.264 Annex-B/NAL summary
with start-code, SPS, PPS, IDR, and non-IDR counts. Add
`--payload-out <file.h264>` to write the concatenated encoded payload bytes as a
raw H.264 artifact for decoder or texture-fixture follow-up work; keep those
generated artifacts under `.\artifacts\` or another ignored output directory.
Use `media inspect-h264` on a saved artifact for file size, SHA-256, and
Annex-B/NAL structure checks. Add `--decode --ffmpeg <path>` when you have a
local FFmpeg executable and want the CLI to run an external first-frame decoder
probe; FFmpeg is not bundled by this repo.

```powershell
dotnet run --project .\src\RustyXr.Companion.Cli -- broker shell-helper start --serial <serial> --rusty-xr-root ..\Rusty-XR --probe-codecs --emit-synthetic-video-metadata --json
dotnet run --project .\src\RustyXr.Companion.Cli -- broker shell-helper start --serial <serial> --rusty-xr-root ..\Rusty-XR --probe-cameras --json
dotnet run --project .\src\RustyXr.Companion.Cli -- broker shell-helper start --serial <serial> --rusty-xr-root ..\Rusty-XR --probe-cameras --probe-camera-open --json
dotnet run --project .\src\RustyXr.Companion.Cli -- broker shell-helper binary-probe --serial <serial> --rusty-xr-root ..\Rusty-XR --json
dotnet run --project .\src\RustyXr.Companion.Cli -- broker shell-helper binary-probe --serial <serial> --rusty-xr-root ..\Rusty-XR --mediacodec-synthetic --encoded-video-frames 4 --encoded-video-width 320 --encoded-video-height 180 --json
dotnet run --project .\src\RustyXr.Companion.Cli -- broker shell-helper binary-probe --serial <serial> --rusty-xr-root ..\Rusty-XR --screenrecord-source --encoded-video-width 320 --encoded-video-height 180 --encoded-video-bitrate 500000 --screenrecord-time-limit 1 --payload-out .\artifacts\broker-shell-helper\screenrecord.h264 --json
dotnet run --project .\src\RustyXr.Companion.Cli -- media inspect-h264 --payload .\artifacts\broker-shell-helper\screenrecord.h264 --json
dotnet run --project .\src\RustyXr.Companion.Cli -- broker shell-helper status --serial <serial> --json
dotnet run --project .\src\RustyXr.Companion.Cli -- broker shell-helper stop --serial <serial> --rusty-xr-root ..\Rusty-XR --no-build --json
```

For a single diagnostic pass that writes a verification bundle and optionally
checks OSC ingress, use:

```powershell
dotnet run --project .\src\RustyXr.Companion.Cli -- broker verify --serial <serial> --osc-host <quest-lan-ip> --out .\artifacts\verify --json
```

For OSC drive ingress, launch the broker profile and send a value over the
Quest LAN IP:

```powershell
dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-broker-apk\catalog\rusty-xr-quest-broker.catalog.json --app rusty-xr-quest-broker --serial <serial> --stop-catalog-apps --install --launch --device-profile broker-smoke-test --runtime-profile broker-osc-drive-ingress --settle-ms 5000 --logcat-lines 1000 --out .\artifacts\verify
dotnet run --project .\src\RustyXr.Companion.Cli -- osc send --host <quest-lan-ip> --port 9000 --address /rusty-xr/drive/radius --arg float:0.75
```

For a clock-aligned comparison bundle, run `broker compare` after the broker
profile and a target app are running. The direct route expects the target app to
listen on UDP `9001` for `/rusty-xr/drive/radius`, include the host send
timestamp in its acknowledgement, and reply on `/rusty-xr/drive/ack` to the
companion's requested acknowledgement port. Use `--skip-direct-osc` or
`--skip-broker-osc` when only one route is active:

```powershell
dotnet run --project .\src\RustyXr.Companion.Cli -- broker compare --quest-host <quest-lan-ip> --serial <serial> --count 16 --interval-ms 250 --out .\artifacts\broker-compare --json
```

The bundle contains `broker-comparison.json`, `broker-comparison.md`,
`direct-osc-roundtrip.csv`, and `broker-osc-stream.csv` when both routes are
available. If `broker status --json` reports OSC ingress disabled, relaunch the
broker with the OSC ingress runtime profile before trusting broker-route counts.

For LSL diagnostics, pass a compatible Windows `lsl.dll`. The local loopback
path does not require a Quest and writes JSON, CSV, Markdown, and PDF reports:

```powershell
dotnet run --project .\src\RustyXr.Companion.Cli -- lsl runtime --lsl-dll <path-to-windows-lsl.dll> --json
dotnet run --project .\src\RustyXr.Companion.Cli -- lsl loopback --lsl-dll <path-to-windows-lsl.dll> --count 16 --interval-ms 100 --out .\artifacts\lsl-loopback --json
```

After the LSL-capable broker APK is running and forwarded, compare WebSocket
latency samples against the broker's forwarded LSL string stream:

```powershell
dotnet run --project .\src\RustyXr.Companion.Cli -- broker forward --serial <serial>
dotnet run --project .\src\RustyXr.Companion.Cli -- lsl broker-roundtrip --serial <serial> --lsl-dll <path-to-windows-lsl.dll> --count 8 --interval-ms 250 --out .\artifacts\lsl-broker --json
```

The public Unity-side target for this broker comparison is
[The Big Red Button Institute](https://github.com/MesmerPrism/the-big-red-button-institute).
It consumes localhost WebSocket events from the broker, exposes the direct Unity
OSC acknowledgement route, and drives one visible button through both paths.

## Working Rules

Keep generated files in ignored output locations:

- Rusty XR APK outputs stay under example `build\` folders.
- Companion verification bundles stay under `artifacts\`.
- APK bytes, signing material, screenshots, logcat dumps, media frames, and
  local caches stay out of git.

When a catalog path breaks, first confirm the sibling repo layout, then run
`workspace guide --json` and compare the reported catalog and APK paths.
