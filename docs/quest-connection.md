---
title: Quest Connection
nav_order: 3
---

# Quest Connection

Rusty XR Companion uses ADB as the baseline Quest connection layer.

The companion can install a managed LocalAppData copy of official Quest
operator tooling:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- tooling install-official
```

That fetches Meta `hzdb`, Android platform-tools, and `scrcpy` from their
upstream release locations and keeps their licenses separate from this MIT
repo.

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

For off-face development sessions, `hzdb` can request a keep-awake hold by
disabling normal wear-sensor sleep behavior for a duration:

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
