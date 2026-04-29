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
The generic companion owns connection, launch, casting, screenshots, and
diagnostic proof; it does not copy app-specific media-service behavior.

## Future Stream Lanes

Planned public lanes:

- structured logcat sessions
- optional LSL stream inventory and status
- generic frame receiver sessions for apps that publish public media metadata
- per-session proof bundles under `artifacts/verify/...`
