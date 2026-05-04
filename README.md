# Rusty XR Companion Apps

Rusty XR Companion Apps is a public Windows-first utility workspace for people
who need a practical bridge between a Windows computer and a Meta Quest device.

The first app is **Rusty XR Companion**, a WPF operator tool with a matching CLI.
It helps you find Quest development tooling, connect a headset over USB or
Wi-Fi ADB, install a user-supplied APK, launch or stop a target app, apply
simple development profiles, install official operator tooling, start a display
cast through `scrcpy`, receive MediaProjection display-composite frames,
send and receive generic OSC UDP probe messages, capture visual proof, pass
catalog runtime profiles as launch extras, manage a
keep-awake proximity hold, read headset and controller battery status, and export
diagnostics that can be shared without a source checkout. The CLI also provides
a clock-aligned broker comparison runner for checking a direct target-app OSC
route against the same OSC drive stream routed through the Quest broker, plus a
broker bio-simulation command for publishing Polar-compatible HR/RR, ECG, and
ACC diagnostic payloads through the broker, and LSL round-trip diagnostics that
write JSON, CSV, Markdown, and PDF report bundles from a user-supplied
Windows `lsl.dll`. In a source workspace, the CLI can also build, push, launch,
disconnect-report, and binary-probe the optional Rusty XR broker ADB shell
helper, including a guarded MediaCodec synthetic-Surface packet probe with
broker metric reporting, host receive counters, a lightweight H.264 NAL
summary, and a guarded shell screenrecord display-source probe; that helper runs
as Android `shell` only because an authorized ADB host launches it. The CLI can
also run the broker app-context raw-luma Camera2 side-channel probe, which
forwards the broker endpoint and a binary payload port, starts a bounded
`raw_luma8` capture inside the broker APK, and validates the received frame
packets on the host. Saved raw-luma artifacts can be inspected for frame
alignment, hashes, luma statistics, and optional PGM contact sheets without
bundling media codecs. The CLI can also start the broker app-context
Camera2-to-platform-H.264 side-channel probe and receive the bounded encoded
packets with the existing H.264 structure summary, then decode a preview frame
through an optional managed FFmpeg media runtime. A broker-local
`broker app-camera-h264-decode-probe` command verifies Android platform
MediaCodec can consume those H.264 packets before the full XR texture path is
attempted.

This repo is designed to work alongside the public
[Rusty XR](https://github.com/MesmerPrism/Rusty-XR) core workspace. Rusty XR
owns reusable Rust contracts and schemas. This repo owns app UX, Windows
release tooling, Quest device operations, and contributor-facing docs.
The sample catalog includes the accepted fullscreen raw-camera composite
profile, a `quad-surface` A/B profile, native passthrough hotload profiles, and
safety-gated strobe profiles. It also includes an environment-depth diagnostics
profile that verifies OpenXR environment-depth provider startup, acquisition,
runtime capture timestamp progression, update cadence, acquire cost, and
confidence-state reporting from logcat. The OSC listener profile enables the
headset diagnostic HUD by default, with a no-overlay companion profile for
separating UDP ingress cost from HUD rendering cost. Strobing profiles are
hazardous and should only be launched with explicit informed opt-in.
The source-workspace guide and sample catalog also cover the public Rusty XR
Quest broker proof-of-concept for localhost WebSocket samples, optional LSL
forwarding, and OSC drive ingress. The matching public Unity Quest example is
[The Big Red Button Institute](https://github.com/MesmerPrism/the-big-red-button-institute),
which compares direct Unity OSC/BLE input against broker-routed stream events
on one visible button.

## Current Scope

- WPF Windows app for Quest install, launch, device profile, cast, and
  diagnostics workflows
- CLI for the same core actions
- general Quest status utilities for headset battery, controller batteries,
  wake state, foreground app, and proximity sensor state
- public sample catalog metadata aligned with the Rusty XR core
  `quest-app-catalog` schema
- managed LocalAppData tool cache for Meta `hzdb`, Android platform-tools,
  `scrcpy`, and optional FFmpeg media preview tooling
- source-workspace guide for sibling Rusty XR and Companion checkouts
- installed `agent-onboarding\` docs in the app and CLI release zips
- GitHub Pages docs and onboarding
- portable Windows release workflow with a guided setup helper
- release app zip bundling for the public Rusty XR Quest camera composite-layer
  APK, with the APK generated from a release asset rather than committed
  source bytes
- catalog install/verify support for local APK paths and GitHub Release asset
  URLs
- runtime-profile launch support for native passthrough style hotload,
  environment-depth diagnostics, and strobe timing experiments published by
  Rusty XR core
- generic OSC UDP send/receive CLI utilities for companion-to-headset adapter
  smoke tests
- clock-aligned `broker compare` diagnostics that write JSON, Markdown, and
  CSV bundles for direct target-app OSC acknowledgements and broker-routed OSC
  WebSocket events
- `broker bio-simulate` diagnostics for Polar-compatible standard Heart Rate
  Measurement and Polar PMD ECG/ACC payloads published as broker stream events
- LSL runtime, local loopback, and broker latency round-trip diagnostics with
  PDF report output
- source-workspace `broker shell-helper` commands for building and launching
  the optional ADB shell-helper stub and checking broker helper status
- source-workspace `broker app-camera-luma-probe` diagnostics for bounded
  app-context Camera2 luma payload delivery over ADB-forwarded TCP
- source-workspace `broker app-camera-h264-probe` diagnostics for bounded
  app-context Camera2-to-platform-H.264 payload delivery over ADB-forwarded TCP
- source-workspace `broker app-camera-h264-decode-probe` diagnostics for
  broker-local Android MediaCodec H.264 consumption with byte-buffer output
- source-workspace `launch-composite-broker-h264-consumer` diagnostics for
  composite-app broker H.264 consumption through a decoder `SurfaceTexture`
  external texture
- source-workspace `launch-composite-broker-h264-openxr-layer` diagnostics for
  broker H.264 decode into hardware buffers, broker Camera2 projection metadata
  latching when available, and drawing by the composite APK's OpenXR GPU-buffer
  path
- source-workspace `launch-composite-broker-h264-stereo-projection`
  diagnostics for two broker H.264 streams decoded into paired hardware buffers
  and handed to the OpenXR stereo projection path
- source-workspace `launch-composite-broker-h264-live-stereo-projection`
  diagnostics for the live-bounded broker H.264 stereo path with schema-2 source
  timestamps, concurrent left/right receive, and source/wire/decode cadence
  reporting before OpenXR stereo projection
- raw-luma artifact inspection for saved broker app-camera payloads
- bundled catalog profiles for the Rusty XR generic diagnostic HUD, including
  a no-overlay OSC A/B profile
- source-workspace broker commands for building, launching, and probing the
  public Quest broker APK proof-of-concept

The repo does **not** commit APK bytes. Release packaging downloads the public
Rusty XR composite-layer APK from a configured release asset, places it beside
the bundled catalog in the portable app zip, and leaves source checkouts small.

## Quick Start

```powershell
git clone https://github.com/MesmerPrism/Rusty-XR-Companion-Apps.git
cd Rusty-XR-Companion-Apps
dotnet build RustyXr.Companion.slnx
dotnet test RustyXr.Companion.slnx
dotnet run --project src/RustyXr.Companion.Cli -- doctor
dotnet run --project src/RustyXr.Companion.Cli -- workspace guide
dotnet run --project src/RustyXr.Companion.App
```

For a source-built single-file app launcher:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1
```

For a separate local dev install that does not overwrite the public release
install:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Install-DevDesktopApp.ps1 -Launch
```

Build the docs site:

```powershell
npm install
npm run pages:build
```

## CLI Examples

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- devices
dotnet run --project src/RustyXr.Companion.Cli -- tooling install-official
dotnet run --project src/RustyXr.Companion.Cli -- wifi enable --serial <usb-serial>
dotnet run --project src/RustyXr.Companion.Cli -- connect --endpoint 192.168.1.25:5555
dotnet run --project src/RustyXr.Companion.Cli -- status --serial <serial>
dotnet run --project src/RustyXr.Companion.Cli -- snapshot --serial <serial> --json
dotnet run --project src/RustyXr.Companion.Cli -- install --serial <serial> --apk C:\path\app.apk
dotnet run --project src/RustyXr.Companion.Cli -- launch --serial <serial> --package com.example.questapp
dotnet run --project src/RustyXr.Companion.Cli -- profile apply --serial <serial> --cpu 2 --gpu 2
dotnet run --project src/RustyXr.Companion.Cli -- cast --serial <serial> --max-size 1280
dotnet run --project src/RustyXr.Companion.Cli -- media reverse --serial <serial> --device-port 8787 --host-port 8787
dotnet run --project src/RustyXr.Companion.Cli -- media receive --port 8787 --out .\artifacts\media-stream --once
dotnet run --project src/RustyXr.Companion.Cli -- osc send --host <quest-lan-ip> --port 9000 --address /rusty-xr/probe --arg string:hello
dotnet run --project src/RustyXr.Companion.Cli -- osc receive --port 9000 --count 1
dotnet run --project src/RustyXr.Companion.Cli -- hzdb proximity keep-awake --serial <serial> --duration-ms 28800000
dotnet run --project src/RustyXr.Companion.Cli -- hzdb screenshot --serial <serial> --out .\artifacts\screenshots
dotnet run --project src/RustyXr.Companion.Cli -- doctor --snapshots --out .\artifacts\diagnostics
dotnet run --project src/RustyXr.Companion.Cli -- workspace guide --root <workspace>
dotnet run --project src/RustyXr.Companion.Cli -- catalog verify --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-minimal --serial <serial> --launch --device-profile perf-smoke-test --out .\artifacts\verify
dotnet run --project src/RustyXr.Companion.Cli -- catalog verify --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile camera-diagnostic-cpu-copy --settle-ms 7000 --logcat-lines 1000 --out .\artifacts\verify
dotnet run --project src/RustyXr.Companion.Cli -- catalog verify --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile camera-stereo-gpu-composite --settle-ms 9000 --logcat-lines 1400 --out .\artifacts\verify
dotnet run --project src/RustyXr.Companion.Cli -- catalog verify --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile camera-stereo-gpu-composite-quad-surface --settle-ms 9000 --logcat-lines 1400 --out .\artifacts\verify
dotnet run --project src/RustyXr.Companion.Cli -- catalog verify --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile environment-depth-diagnostics --settle-ms 9000 --logcat-lines 1400 --out .\artifacts\verify
dotnet run --project src/RustyXr.Companion.Cli -- catalog verify --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile media-projection-stream --media-receiver --settle-ms 7000 --logcat-lines 1000 --out .\artifacts\verify
dotnet run --project src/RustyXr.Companion.Cli -- catalog verify --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-broker --serial <serial> --stop-catalog-apps --install --launch --device-profile broker-smoke-test --runtime-profile broker-osc-drive-ingress --settle-ms 5000 --logcat-lines 1000 --out .\artifacts\verify
dotnet run --project src/RustyXr.Companion.Cli -- broker forward --serial <serial>
dotnet run --project src/RustyXr.Companion.Cli -- broker status --json
dotnet run --project src/RustyXr.Companion.Cli -- broker streams --json
dotnet run --project src/RustyXr.Companion.Cli -- broker sample --subscribe --json
dotnet run --project src/RustyXr.Companion.Cli -- broker verify --serial <serial> --osc-host <quest-lan-ip> --out .\artifacts\verify --json
dotnet run --project src/RustyXr.Companion.Cli -- broker compare --quest-host <quest-lan-ip> --serial <serial> --count 16 --interval-ms 250 --out .\artifacts\broker-compare --json
dotnet run --project src/RustyXr.Companion.Cli -- broker bio-simulate --serial <serial> --count 8 --interval-ms 250 --out .\artifacts\broker-bio-sim --json
dotnet run --project src/RustyXr.Companion.Cli -- broker app-camera-luma-probe --serial <serial> --camera-id <id> --frame-count 2 --payload-out .\artifacts\broker-app-camera\luma.raw --json
dotnet run --project src/RustyXr.Companion.Cli -- media inspect-raw-luma --payload .\artifacts\broker-app-camera\luma.raw --width 720 --height 480 --contact-sheet .\artifacts\broker-app-camera\luma.pgm --json
dotnet run --project src/RustyXr.Companion.Cli -- broker app-camera-h264-probe --serial <serial> --camera-id <id> --capture-ms 900 --max-packets 12 --payload-out .\artifacts\broker-app-camera\camera.h264 --json
dotnet run --project src/RustyXr.Companion.Cli -- broker app-camera-h264-decode-probe --serial <serial> --camera-id <id> --capture-ms 900 --max-packets 12 --json
dotnet run --project src/RustyXr.Companion.Cli -- media inspect-h264 --payload .\artifacts\broker-app-camera\camera.h264 --json
dotnet run --project src/RustyXr.Companion.Cli -- broker shell-helper start --serial <serial> --rusty-xr-root ..\Rusty-XR --probe-codecs --emit-synthetic-video-metadata --json
dotnet run --project src/RustyXr.Companion.Cli -- broker shell-helper start --serial <serial> --rusty-xr-root ..\Rusty-XR --probe-cameras --json
dotnet run --project src/RustyXr.Companion.Cli -- broker shell-helper start --serial <serial> --rusty-xr-root ..\Rusty-XR --probe-cameras --probe-camera-open --json
dotnet run --project src/RustyXr.Companion.Cli -- broker shell-helper binary-probe --serial <serial> --rusty-xr-root ..\Rusty-XR --json
dotnet run --project src/RustyXr.Companion.Cli -- broker shell-helper binary-probe --serial <serial> --rusty-xr-root ..\Rusty-XR --mediacodec-synthetic --encoded-video-frames 4 --encoded-video-width 320 --encoded-video-height 180 --json
dotnet run --project src/RustyXr.Companion.Cli -- broker shell-helper binary-probe --serial <serial> --rusty-xr-root ..\Rusty-XR --screenrecord-source --encoded-video-width 320 --encoded-video-height 180 --encoded-video-bitrate 500000 --screenrecord-time-limit 1 --payload-out .\artifacts\broker-shell-helper\screenrecord.h264 --json
dotnet run --project src/RustyXr.Companion.Cli -- media inspect-h264 --payload .\artifacts\broker-shell-helper\screenrecord.h264 --json
dotnet run --project src/RustyXr.Companion.Cli -- broker shell-helper status --serial <serial> --json
dotnet run --project src/RustyXr.Companion.Cli -- broker shell-helper stop --serial <serial> --rusty-xr-root ..\Rusty-XR --no-build --json
dotnet run --project src/RustyXr.Companion.Cli -- lsl runtime --lsl-dll <path-to-windows-lsl.dll> --json
dotnet run --project src/RustyXr.Companion.Cli -- lsl loopback --lsl-dll <path-to-windows-lsl.dll> --count 16 --out .\artifacts\lsl-loopback --json
dotnet run --project src/RustyXr.Companion.Cli -- lsl broker-roundtrip --serial <serial> --lsl-dll <path-to-windows-lsl.dll> --count 8 --out .\artifacts\lsl-broker --json
dotnet run --project src/RustyXr.Companion.Cli -- osc send --host <quest-lan-ip> --port 9000 --address /rusty-xr/drive/radius --arg float:0.75
```

## Documentation

- [Docs home](docs/index.md)
- [Getting started](docs/getting-started.md)
- [Quest connection](docs/quest-connection.md)
- [APK install and launch](docs/apk-install-launch.md)
- [Source workspace](docs/source-workspace.md)
- [Streaming](docs/streaming.md)
- [Diagnostics](docs/diagnostics.md)
- [Release workflow](docs/release-workflow.md)
- [Dev and release channels](docs/dev-release-channels.md)
- [Git LFS and assets](docs/lfs-and-assets.md)
- [Contributing](CONTRIBUTING.md)

## Release Shape

The current release workflow publishes:

- `RustyXrCompanion-Setup.exe`
- `RustyXrCompanion-win-x64.zip`
- `rusty-xr-companion-cli-win-x64.zip`
- `SHA256SUMS.txt`

The setup helper installs the portable app under the user's LocalAppData
programs folder, creates a Start Menu launcher with the Companion icon, and
registers a per-user Windows uninstall entry. The installer displays both the
install folder and launcher location before it starts:

- `%LOCALAPPDATA%\Programs\RustyXrCompanion`
- `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Rusty XR Companion\Rusty XR Companion.lnk`

The app zip includes a bundled `catalogs\` catalog plus the public Rusty XR
Quest camera composite-layer and broker APKs under `catalogs\apks\`. On first
launch the WPF app auto-loads that catalog so the composite-layer and broker
examples are already in the APK list for install and launch on a connected
Quest.
Both the app zip and CLI zip include `agent-onboarding\AGENTS.md` plus
`agent-onboarding\source-workspace.md` so local agents can start from a
released install even before the source repos are checked out.

Published installs check GitHub Releases on startup and update themselves from
the latest portable app zip when a newer release exists. It can be signed by
the release workflow when a signing certificate is configured in GitHub
secrets; the app zip also carries the same signed helper as
`RustyXrCompanion-Uninstall.exe` for Windows Settings uninstall.

MSIX and `.appinstaller` support are intentionally documented as the next
packaging lane. The portable installer is the low-friction first release path
because this version does not need Windows package identity.

## License

MIT for this repository. Upstream tools and user-supplied APKs keep their own
licenses and distribution rules. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
