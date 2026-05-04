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

## Broker H.264 Preview Decode

The broker app-camera H.264 path is a bounded diagnostic lane for validating
encoded camera payload transport before a target OpenXR app owns decode-to-texture
and layer submission. The companion can start the broker stream, save the
elementary H.264 payload, summarize the RXYRVID1 packet framing, and decode a
single PNG preview frame when FFmpeg is available.

Install the optional media runtime when `ffmpeg.exe` is not already on `PATH`:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- tooling install-media
dotnet run --project src\RustyXr.Companion.Cli -- tooling media-status --latest
```

The installer downloads a Windows x64 LGPL shared FFmpeg build into the
managed LocalAppData cache, verifies SHA-256, records metadata, and classifies
`ffmpeg -version` output for GPL/nonfree flags. The app and CLI release zips
do not bundle FFmpeg binaries.

Capture and decode a preview:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- broker app-camera-h264-probe --serial <serial> --camera-id <id> --capture-ms 900 --max-packets 12 --payload-out .\artifacts\broker-app-camera\camera.h264 --json
dotnet run --project src\RustyXr.Companion.Cli -- media decode-h264-preview --payload .\artifacts\broker-app-camera\camera.h264 --out .\artifacts\broker-app-camera\camera-preview.png --json
```

The WPF **Streams** tab exposes the same flow through **Capture H.264 Preview**
and **Decode H.264 Preview**. Leave the FFmpeg path field blank for managed
runtime or `PATH` discovery, or set it to an explicit user-supplied
`ffmpeg.exe`.

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

## Broker OSC Drive

The Rusty XR broker proof-of-concept exposes a sidecar OSC ingress profile.
After building `examples\quest-broker-apk` in the sibling Rusty XR checkout,
launch the broker and send a drive value to the Quest LAN IP:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-broker-apk\catalog\rusty-xr-quest-broker.catalog.json --app rusty-xr-quest-broker --serial <serial> --stop-catalog-apps --install --launch --runtime-profile broker-osc-drive-ingress --settle-ms 5000 --logcat-lines 1000 --out .\artifacts\verify
dotnet run --project src\RustyXr.Companion.Cli -- broker forward --serial <serial>
dotnet run --project src\RustyXr.Companion.Cli -- broker status --json
dotnet run --project src\RustyXr.Companion.Cli -- broker streams --json
dotnet run --project src\RustyXr.Companion.Cli -- broker sample --subscribe --json
dotnet run --project src\RustyXr.Companion.Cli -- broker verify --serial <serial> --osc-host <quest-lan-ip> --out .\artifacts\verify --json
dotnet run --project src\RustyXr.Companion.Cli -- osc send --host <quest-lan-ip> --port 9000 --address /rusty-xr/drive/radius --arg float:0.75
```

The broker turns accepted OSC packets into localhost WebSocket `osc_drive`
events. The public Unity Quest target for this comparison is
[The Big Red Button Institute](https://github.com/MesmerPrism/the-big-red-button-institute),
which consumes the broker stream events and exposes the direct Unity OSC
acknowledgement route used by `broker compare`.

## Broker Comparison Runner

Use `broker compare` when you need one bundle that compares a direct target-app
OSC route with the broker-routed OSC/WebSocket route:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- broker compare --quest-host <quest-lan-ip> --serial <serial> --count 16 --interval-ms 250 --out .\artifacts\broker-compare --json
```

The direct route sends `/rusty-xr/drive/radius` to UDP `9001` with
`value01`, `sequence`, `host_send_unix_ns`, and `reply_port` arguments. A target
app that implements the acknowledgement profile replies on
`/rusty-xr/drive/ack` with sequence, host send time, target receive time,
target acknowledgement send time, value, and accepted-pulse state.

The comparison runner estimates target-minus-host clock offset with the same
four-timestamp shape used by NTP-style round-trip probes, then reports aligned
host-to-target and target-to-host timing. The recommended offset is the median
offset from the lowest-RTT quartile, which avoids letting a single delayed UDP
packet dominate the assessment.

Use
[The Big Red Button Institute](https://github.com/MesmerPrism/the-big-red-button-institute)
when you want a complete Unity-side Quest scene that accepts the direct route
on UDP `9001` and the broker route through localhost WebSocket stream events.

For route-specific checks:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- broker compare --quest-host <quest-lan-ip> --skip-broker-osc --out .\artifacts\broker-compare --json
dotnet run --project src\RustyXr.Companion.Cli -- broker compare --quest-host <quest-lan-ip> --serial <serial> --skip-direct-osc --out .\artifacts\broker-compare --json
```

## Broker LSL Probe

The broker can forward accepted WebSocket latency samples to LSL when the APK
was built with a compliant Android `liblsl.so`. The public source does not
vendor that native library. When LSL is packaged, `broker status` reports
`lsl.gateway`, `publisher: native-lsl`, stream name
`rusty_xr_broker_latency`, and stream type `rusty.xr.latency`.

Send one broker sample and inspect the acknowledgement:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- broker sample --path lsl_probe --bytes 128 --json
```

The `latency_ack` payload should show `lsl_forwarded: true`. A Windows LSL
receiver can independently resolve the stream by name and pull the string
sample. If resolution fails, check that the Quest and Windows host are on the
same reachable network and that local firewall policy allows LSL multicast and
inbound traffic for the receiving process.

## LSL Round-Trip Diagnostics

The companion can run LSL diagnostics from the CLI when given a compatible
Windows `lsl.dll`. The public companion does not vendor native LSL binaries;
pass the DLL path explicitly, set `RUSTY_XR_LSL_DLL`, place `lsl.dll` beside
the executable, or put it on `PATH`.

Check native runtime loading first:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- lsl runtime --lsl-dll <path-to-windows-lsl.dll> --json
```

Use local loopback to validate the Windows receiver path without a Quest. The
command creates a temporary double64 LSL outlet and inlet, opens the inlet,
primes the stream, sends sequenced samples, estimates LSL clock correction, and
writes JSON, CSV, Markdown, and PDF outputs:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- lsl loopback --lsl-dll <path-to-windows-lsl.dll> --count 16 --interval-ms 100 --out .\artifacts\lsl-loopback --json
```

Use broker round-trip after launching an LSL-capable broker APK and forwarding
its WebSocket endpoint. This route sends broker latency samples over WebSocket,
pulls the matching `rusty_xr_broker_latency` LSL string samples, and reports
host send to broker receive, broker processing, LSL receive delay, and clock
correction uncertainty:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- broker forward --serial <serial>
dotnet run --project src\RustyXr.Companion.Cli -- lsl broker-roundtrip --serial <serial> --lsl-dll <path-to-windows-lsl.dll> --count 8 --interval-ms 250 --out .\artifacts\lsl-broker --json
```

Pass `--no-pdf` when only machine-readable JSON and CSV are needed.

## Broker Bio Simulation

Use `broker bio-simulate` to publish synthetic Polar-compatible payloads through
the broker as generic stream events:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- broker bio-simulate --serial <serial> --count 8 --interval-ms 250 --out .\artifacts\broker-bio-sim --json
```

The command publishes:

- `bio:polar_hr_rr`: standard BLE Heart Rate Measurement notifications with BPM
  and RR intervals
- `bio:polar_ecg`: Polar PMD Data notifications for uncompressed ECG frames
- `bio:polar_acc`: Polar PMD Data notifications for uncompressed accelerometer
  frames

Each payload includes the service UUID, characteristic UUID, notification mode,
raw payload bytes as base64, decoded summary fields, and the intended public LSL
stream schema. This is a protocol-level diagnostic stream through the broker; it
does not make the Windows computer advertise as a Bluetooth peripheral.

Subscribe to one lane while publishing targeted samples:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- broker subscribe --stream bio:polar_ecg --listen-ms 6000 --json
dotnet run --project src\RustyXr.Companion.Cli -- broker bio-simulate --serial <serial> --count 2 --skip-hr --skip-acc --json
```

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
- optional LSL stream inventory and receiver-side status
- native BLE adapter validation against real sensors or platform BLE peripheral
  simulation where the host platform supports it
- per-session proof bundles under `artifacts/verify/...`
