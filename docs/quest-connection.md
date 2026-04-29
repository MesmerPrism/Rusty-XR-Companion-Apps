---
title: Quest Connection
nav_order: 3
---

# Quest Connection

Rusty XR Companion uses ADB as the baseline Quest connection layer.

## USB

USB is the best first check because it proves the Windows machine, headset,
cable, developer mode, and trust prompt are all aligned.

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- devices
```

The WPF app exposes the same flow through **Refresh Devices** and the Quest
selector.

## Wi-Fi ADB

After USB trust is established, connect to a known endpoint:

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
