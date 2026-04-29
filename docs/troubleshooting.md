---
title: Troubleshooting
nav_order: 11
---

# Troubleshooting

## ADB Is Missing

Install Android Platform Tools or set:

```powershell
$env:RUSTY_XR_ADB = "C:\path\to\adb.exe"
```

Then run:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- doctor
```

## Device Does Not Appear

- confirm Quest developer mode is enabled
- reconnect USB
- accept the headset's USB debugging prompt
- run `adb devices -l`
- try a different cable or USB port

## Wi-Fi ADB Fails

- verify USB ADB works first
- confirm Windows and Quest are on the same network
- pass the endpoint as `host:5555`
- run `devices` after `connect`

## Cast Does Not Start

- install `scrcpy`
- add `scrcpy.exe` to `PATH`
- confirm ADB can see the selected serial
- try a smaller `--max-size`

## Setup Helper Is Blocked

The setup helper may be self-signed in preview releases. Some Windows security
policies can block a downloaded helper even when the binary is signed. Use the
portable zip or source build path if that happens.
