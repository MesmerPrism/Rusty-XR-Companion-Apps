---
title: Streaming
nav_order: 5
---

# Streaming

The first stream lane is display casting through `scrcpy`.

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- cast --serial <serial> --max-size 1280
```

The WPF app exposes this as **Start Display Cast**.

## Tool Boundary

Rusty XR Companion does not reimplement video streaming. It starts and manages
external tools where that is the better open-source base. Keep upstream tool
licenses and update paths explicit when packaging or documenting a release.

## Future Stream Lanes

Planned public lanes:

- structured logcat sessions
- optional LSL stream inventory and status
- generic frame receiver sessions for apps that publish public media metadata
- per-session proof bundles under `artifacts/verify/...`
