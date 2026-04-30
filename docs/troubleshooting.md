---
title: Troubleshooting
nav_order: 11
---

# Troubleshooting

## ADB Is Missing

Run the managed tooling install, install Android Platform Tools manually, or
set:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- tooling install-official
```

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
- try `wifi enable --serial <usb-serial>` before manually entering an endpoint
- pass the endpoint as `host:5555`
- run `devices` after `connect`

## Cast Does Not Start

- run `tooling install-official`
- add `scrcpy.exe` to `PATH`
- confirm ADB can see the selected serial
- try a smaller `--max-size`

## Immersive Example Stays Loading Or Black

First isolate the OpenXR and camera route from MediaProjection. Launch the
example with display capture disabled, then inspect logcat:

```powershell
adb shell am start -a android.intent.action.MAIN -c com.oculus.intent.category.VR -n com.example.rustyxr.composite/.CompositeLayerActivity --ez rustyxr.camera true --ez rustyxr.mediaProjection false --es rustyxr.cameraTier cpu-diagnostic-flat-copy --ei rustyxr.cameraWidth 1280 --ei rustyxr.cameraHeight 1280 --ei rustyxr.cameraPreferredSquare 1280 --ei rustyxr.cameraMaxDimension 1920 --ef rustyxr.cameraRawOverlayOverscan 1.06 --ef rustyxr.xrRenderScale 0.75 --ei rustyxr.xrFixedFoveationLevel 0
```

Healthy OpenXR bring-up logs include:

- `Rusty XR initialized Android OpenXR loader with Activity context`
- `OpenXR state READY`
- `OpenXR state SYNCHRONIZED`
- `OpenXR state VISIBLE`
- `OpenXR state FOCUSED`
- swapchain creation
- recurring `OpenXR frame` messages
- `Headset camera capture session running`
- `Rusty XR received headset camera frame`
- `Rusty XR uploaded diagnostic flat camera copy frame` for the CPU diagnostic
  profile, or `Rusty XR GPU-sampled diagnostic camera surface` for the GPU
  buffer probe profile

If the app only reaches `OpenXR state IDLE`, look for runtime warnings about a
legacy/non-context OpenXR client or `xrCreateSession: Activity is not yet in
the ready state`. That usually means the APK passed an application context
where Quest expects the current Activity context for OpenXR loader or instance
creation.

If OpenXR reaches `FOCUSED`, camera frames arrive, and upload logs continue,
the renderer and camera route are loaded. A later stall with MediaProjection
enabled is usually a headset consent or selector step, not a renderer failure.

## MediaProjection Selector Is Stuck

Some Quest system builds show `Select view you want to share` after the first
capture consent prompt. Select `Entire view`, then press `Share` in the
headset. The companion cannot reliably hit this selector with shell taps or
UIAutomator; treat it as a manual user step.

A one-frame receiver can also close the socket after proving the first payload.
In that mode, an app-side broken pipe after `MediaProjection stream frame` does
not by itself mean capture failed.

## Proximity Or Screenshot Commands Fail

- run `tooling status --latest` and confirm managed `hzdb` is installed
- confirm the selected serial is visible through `devices`
- use `hzdb status --serial <serial>` to read `dumpsys vrpowermanager`
- use `hzdb screenshot --method screencap` first, then try `--method metacam`

## Setup Helper Is Blocked

The setup helper may be self-signed in preview releases. Some Windows security
policies can block a downloaded helper even when the binary is signed. Use the
portable zip or source build path if that happens.
