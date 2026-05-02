---
title: Streaming
nav_order: 5
---

# Streaming

The first stream lane is display casting through `scrcpy`. Install the managed
tool cache if `scrcpy` is not already on `PATH`:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- tooling install-official
```

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- cast --serial <serial> --max-size 1280
```

The WPF app exposes this as **Start Display Cast**.

## MediaProjection Frame Receiver

Examples that stream final display-composite frames can use the built-in
receiver. It implements the same length-prefixed JSON header plus payload
protocol as the Rusty XR core media-pipeline tool. For camera-driven custom
layers, this stream is for Windows/operator inspection of the final headset
screen; it is not the layer's camera source.

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- media reverse --serial <serial> --device-port 8787 --host-port 8787
dotnet run --project src\RustyXr.Companion.Cli -- media receive --port 8787 --out .\artifacts\media-stream --once
```

The Quest app connects to `127.0.0.1:8787`; `adb reverse` forwards that socket
to the Windows receiver. MediaProjection still requires the headset popup to be
accepted by the user in normal app flows.

When using `--once`, the receiver exits after the first frame. The target app
may then log a broken pipe because the proof receiver closed the socket; use
the saved frame and matching receiver metadata as the success signal for that
short validation mode.

Some Quest builds show a second MediaProjection panel titled `Select view you
want to share`. The user must select `Entire view` in the headset and then
press `Share`. ADB shell taps and UIAutomator inspection are not reliable for
that panel because the VR selector focus is owned by system UI, so the
companion records diagnostics but does not claim it can clear that prompt.

Catalog verification can arm the receiver automatically:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- catalog verify --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --runtime-profile media-projection-stream --media-receiver --settle-ms 7000 --logcat-lines 1000 --out .\artifacts\verify
```

## OSC UDP Probe

The companion CLI can send and receive generic OSC messages for lightweight
control/sensor adapter tests. OSC uses UDP, so test it over the Quest LAN IP;
the `adb reverse` helper used by MediaProjection is a TCP path and is not used
for this probe.

Launch a Rusty XR app profile that enables an OSC listener, then send a probe
message from the companion:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- catalog verify --path samples\quest-session-kit\apk-catalog.example.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --runtime-profile osc-udp-listener --settle-ms 5000 --logcat-lines 1000 --out .\artifacts\verify
dotnet run --project src\RustyXr.Companion.Cli -- osc send --host <quest-lan-ip> --port 9000 --address /rusty-xr/probe --arg string:hello
```

The headset log should include `Rusty XR OSC packet received`. To test the
Windows receive side instead, run:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- osc receive --host 0.0.0.0 --port 9000 --count 1
```

The `osc-udp-listener` profile enables the headset diagnostic HUD. Use
`osc-udp-listener-no-overlay` when you want to measure UDP ingress without
drawing the HUD.

## Visual Inspection

For agent and operator verification, capture still frames without routing a
downstream app-specific media pipeline:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- hzdb screenshot --serial <serial> --out .\artifacts\screenshots
```

The default screenshot path uses ADB `exec-out screencap -p` first. Pass
`--method metacam` when you specifically need `hzdb`'s metacam capture path.

## Tool Boundary

Rusty XR Companion does not reimplement video streaming. It starts and manages
external tools where that is the better open-source base. Keep upstream tool
licenses and update paths explicit when packaging or documenting a release.

Display-composite or MediaProjection frame streams should be exposed by the
target APK through a public adapter contract before the companion consumes them.
The generic companion owns connection, launch extras, casting, screenshots,
frame receipt, and diagnostic proof; it does not copy app-specific renderer
behavior or app-visible camera acquisition.

## Future Stream Lanes

Planned public lanes:

- richer OSC endpoint inventory and proof bundles
- optional LSL stream inventory and status
- per-session proof bundles under `artifacts/verify/...`
