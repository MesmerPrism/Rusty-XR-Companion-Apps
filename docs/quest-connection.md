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

## Snapshot

Snapshots collect safe, generic headset state:

- model
- battery summary
- wakefulness/display state
- foreground package/activity when Android reports it

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- snapshot --serial <serial>
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

Use this state carefully. `Virtual proximity state: CLOSE` means the keep-awake
hold is active. `Virtual proximity state: DISABLED` means normal wear-sensor
sleep behavior is back in control:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- hzdb proximity normal --serial <serial>
```
