---
title: Quest Connection
nav_order: 3
---

# Quest Connection

Rusty XR Companion uses ADB as the baseline Quest connection layer.

## Prerequisites And Gates

Keep three gates separate when diagnosing a connection:

```text
Developer Mode gate
  the headset exposes ADB workflows only after Developer Mode is enabled

ADB authorization gate
  the user authorizes this Windows machine's ADB key in the headset prompt

ADB transport gate
  USB or Wi-Fi carries the already-authorized ADB session
```

For a normal retail Quest, enable Developer Mode through the supported Meta
account/mobile-app flow before expecting ADB to appear. If Developer Mode is
off, the headset may only charge over USB, may expose no ADB-class interface,
and may never show the "Allow USB debugging" prompt.

After Developer Mode is on, a new Windows machine still has to be authorized by
accepting the headset debugging prompt. Once that host is authorized, Companion
can install, launch, forward ports, run shell diagnostics, and enable Wi-Fi ADB
from USB. The Meta developer account is not consulted for each ADB command, but
the authorized ADB transport must be active.

Wi-Fi ADB is not a first bootstrap path. It is a transport handoff for a
working ADB relationship. Companion's **Enable Wi-Fi ADB From USB** action uses
USB ADB first, asks the headset to listen on TCP port `5555`, reads the
headset Wi-Fi address, and reconnects over TCP.

A browser download or normal installed headset APK cannot enable Developer
Mode, authorize this Windows machine, become Android `shell`, or turn on Wi-Fi
ADB from a locked headset.

The companion can install a managed LocalAppData copy of official Quest
operator tooling:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- tooling install-official
```

That fetches Meta VR CLI / `hzdb` compatibility tooling, Android
platform-tools, and `scrcpy` from their upstream release locations and keeps
their licenses separate from this MIT repo. The current public manual Meta
route is `npx -y metavr`; the managed cache keeps the Windows `hzdb.exe`
compatibility binary because `metavr` pulls that package as its platform
dependency.

The optional media runtime is separate from Quest connection tooling. Install
it only when you want saved H.264 preview decode through FFmpeg:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- tooling install-media
```

That command downloads a verified Windows x64 LGPL shared FFmpeg build into
the managed LocalAppData cache. The public app and CLI zips do not bundle
FFmpeg binaries.

## USB

USB is the best first check because it proves the Windows machine, headset,
cable, developer mode, and trust prompt are all aligned.

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- devices
```

The WPF app exposes the same flow through **Refresh Devices** and the Quest
selector.

## Wi-Fi ADB

After USB trust is established, the companion can ask the USB-connected Quest to
listen on TCP port `5555`, read the headset's Wi-Fi IP, and connect to it:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- wifi enable --serial <usb-serial>
```

The WPF app exposes the same action as **Enable Wi-Fi ADB From USB**. You can
also connect to a known endpoint directly:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- connect --endpoint 192.168.1.25:5555
```

Then list devices again. If multiple devices are present, pass `--serial` for
every action.

If Wi-Fi ADB worked before but fails after a reboot, reconnect over USB and run
the Wi-Fi enable flow again. Classic `adb tcpip` sessions commonly do not
survive headset reboot or transport resets.

## Troubleshooting Meaning

| Symptom | Likely meaning |
| --- | --- |
| No device appears over USB | Developer Mode may be off, the cable may be charge-only, the headset may be asleep, or Windows driver/tooling selection may be wrong. |
| Device appears as `unauthorized` | The headset prompt was denied, missed, or USB debugging authorizations were revoked. |
| USB ADB works but Wi-Fi connect fails | Wrong headset IP, different network/subnet, port `5555` not listening, or local network/firewall routing issue. |
| Shell helper status is absent | The broker is not running, the helper was not started by ADB, the helper exited, or the broker endpoint is not forwarded/visible. |

## Device Status

The WPF app has a **Device Status** tab for the selected Quest. It reads the
same safe, generic state as the CLI:

- model
- headset battery summary
- left and right controller battery summaries when `dumpsys tracking` reports them
- wakefulness, interactivity, and display state
- proximity sensor state from `dumpsys vrpowermanager`
- foreground package/activity when Android reports it

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- status --serial <serial>
dotnet run --project src/RustyXr.Companion.Cli -- snapshot --serial <serial> --json
```

The snapshot is not a substitute for looking inside the headset. If an app
claims to be foregrounded but the headset view is blocked by a system dialog,
clear the visual blocker before diagnosing app logic.

## Proximity Keep-Awake

For off-face development sessions, the companion's `hzdb` compatibility command
can request a keep-awake hold by disabling normal wear-sensor sleep behavior
for a duration:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- hzdb proximity keep-awake --serial <serial> --duration-ms 28800000
dotnet run --project src/RustyXr.Companion.Cli -- hzdb status --serial <serial>
```

The WPF **Device Status** tab exposes the same actions as **Proximity Off / Keep Awake**, **Proximity On / Normal**, and **Read Proximity Status**.

Use this state carefully. `Virtual proximity state: CLOSE` means the keep-awake
hold is active. `Virtual proximity state: DISABLED` means normal wear-sensor
sleep behavior is back in control:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- hzdb proximity normal --serial <serial>
```

The wake helper treats wake and keep-awake as linked operations for off-face
development. `hzdb wake` first sends the Meta wake request, then applies a
keep-awake hold so the headset does not immediately fall back into a limbo or
standby state. In the WPF app, normal snapshot auto-refresh is a passive
watcher. It observes wake/proximity state and never adopts a passive
`Virtual proximity state: CLOSE` as policy. The active keep-awake watchdog
starts only when the app itself requests **Wake** or **Proximity Off / Keep
Awake**, or when an earlier app-owned watchdog restore is still active. While
that watchdog is active, the snapshot timer uses a short interval and reapplies
the operator-requested hold until **Proximity On / Normal** or **Sleep** turns
it off. Turning normal auto-refresh off does not disable an active watchdog;
the watchdog is stopped only by the explicit normal/sleep actions.

For broker workflows that already start the optional ADB shell helper, the
helper can run an additional shell-side proximity watchdog:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- broker shell-helper start --serial <serial> --rusty-xr-root ..\Rusty-XR --no-broker-report --skip-status --proximity-watchdog --proximity-watchdog-until-stopped --proximity-watchdog-ensure-stay-awake --json
dotnet run --project src\RustyXr.Companion.Cli -- broker shell-helper stop --serial <serial> --rusty-xr-root ..\Rusty-XR --no-build --json
```

The shell-side and WPF watchdogs are designed to be idempotent. Both preserve
`Virtual proximity state: CLOSE`, and neither sends normal-proximity
`automation_disable` while preserving a hold. With
`--proximity-watchdog-ensure-stay-awake`, the shell helper also reinforces the
power side of camera availability by reapplying `svc power stayon true` and
issuing `KEYCODE_WAKEUP` when power readback drifts. Stop the shell helper
before intentionally restoring normal wear-sensor behavior.
