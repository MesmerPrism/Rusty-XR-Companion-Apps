---
title: Rusty XR Companion
nav_order: 1
---

# Rusty XR Companion

Rusty XR Companion is a public Windows utility for Quest development and
operator workflows.

Use it when you need to:

- confirm ADB, optional Quest tooling, and optional casting tools are available
- install managed `hzdb`, Android platform-tools, `scrcpy`, and optional
  FFmpeg media preview tooling
- find Quest devices over ADB
- connect to a Quest over Wi-Fi ADB
- read headset battery, controller batteries, wake state, and proximity state
- install a local APK
- launch or stop a target app, including catalog runtime profiles
- apply simple headset development settings
- start a display cast through `scrcpy`
- capture bounded broker H.264 camera payloads and decode preview frames when
  FFmpeg is available
- send and receive generic OSC UDP probe messages
- launch and probe the public Rusty XR Quest broker proof-of-concept from a
  sibling source checkout
- capture headset screenshots and toggle proximity keep-awake state
- export diagnostics that another developer can inspect

The app is intentionally generic. It does not bundle a private runtime. The
published Windows install includes the public Rusty XR camera composite-layer
example, including the diagnostic HUD, OSC listener profiles, passthrough
hotload profiles, and safety-gated strobe profiles. It still supports local APK
paths, public catalog files, and the CLI.
The source-workspace path also covers the public broker APK proof for localhost
WebSocket samples, optional LSL forwarding, and OSC drive events. The matching
public Unity Quest example is
[The Big Red Button Institute](https://github.com/MesmerPrism/the-big-red-button-institute),
which compares direct Unity OSC/BLE input against broker-routed stream events
on one visible button.

## Start Here

1. [Consumer quick start](consumer-quick-start.md)
2. [Getting started](getting-started.md)
3. [Quest connection](quest-connection.md)
4. [APK install and launch](apk-install-launch.md)
5. [Source workspace](source-workspace.md)
6. [Diagnostics](diagnostics.md)
7. [Rusty XR core contracts](rusty-xr-core-contracts.md)
8. [Download and release workflow](download.md)
9. [Dev and release channels](dev-release-channels.md)
