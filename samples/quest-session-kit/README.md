# Quest Session Kit Samples

This folder contains public placeholder metadata for adapting the companion to
your own Quest apps.

The repo does not commit APK bytes. Keep source-checkout APKs local, choose
them from the WPF app, or publish large public payloads as GitHub Release
assets. The Windows release workflow can copy an approved generated APK into
the app zip without storing that binary in git.

Files:

- `apk-catalog.example.json`

The sample uses `schemaVersion: "rusty.xr.quest-app-catalog.v1"`, matching the
public Rusty XR core schema exporter. Keep APK bytes out of the sample folder.

The sample includes `rusty-xr-quest-minimal` and
`rusty-xr-quest-composite-layer` entries that point at sibling Rusty-XR
checkout ignored build outputs:

```text
../../../Rusty-XR/examples/quest-minimal-apk/build/outputs/rusty-xr-quest-minimal-debug.apk
../../../Rusty-XR/examples/quest-composite-layer-apk/build/outputs/rusty-xr-quest-composite-layer-debug.apk
```

Build the APKs in Rusty-XR first, then install or verify them through the
Companion catalog commands. The composite-layer example uses runtime profiles
for `synthetic-composite-layer`, `camera-source-diagnostics`,
`camera-diagnostic-cpu-copy`, `camera-gpu-buffer-probe`,
`camera-stereo-gpu-composite`,
`camera-stereo-gpu-composite-quad-surface`, `environment-depth-diagnostics`,
and optional `media-projection-stream`.

For the full sibling-repo flow, run:

```powershell
dotnet run --project src\RustyXr.Companion.Cli -- workspace guide --root <workspace>
```

The guide prints the expected Rusty XR build outputs, catalog paths, and
verification commands for local agents.

The Tier 1 CPU profile is a mono diagnostic flat camera copy with a throttled
CPU upload cadence. The GPU-buffer probe requests Camera2 `PRIVATE` hardware
buffers, imports them for Vulkan sampling with CPU fallback disabled, and logs
any remaining projection blockers instead of claiming true stereo or
camera/view alignment without active metadata-backed projection. The final
stereo profile is the accepted public raw-camera reference for the tested Quest
Camera2 provider: one projection-status line must report paired left/right GPU
buffers, `activeTier=gpu-projected`, `alignedProjection=true`, zero CPU
diagnostic upload, OpenXR focus/frame evidence, platform or public
estimated-profile pose metadata, explicit per-eye texture orientation, and
manual visual acceptance. For new headset firmware or device variants, rerun
camera diagnostics and treat the manual visual gate as open until the feed is
upright, the per-eye content is not swapped or divergent, and the public soft
projection-feedback border is visible.
The quad-surface profile is an A/B comparison path for projection geometry and
color handling; it keeps the same stereo GPU-buffer source and manual visual
gate while selecting `rustyxr.cameraProjectionMode=quad-surface` to reconstruct
the content-surface UV a head-anchored quad would rasterize. It also selects
`rustyxr.cameraColorMode=external-rgb` with a small public
contrast/brightness lift so the projected camera feed and border feedback use
the same normalized shader color domain. The profile is useful for
collaboration, but it is not yet the final performance or color reference;
keep the manual visual gate closed while investigating remaining performance
and tone differences.

The environment-depth diagnostics profile starts only the OpenXR
environment-depth path. Companion validation checks the latest
`Rusty XR environment depth status` log line for provider support, swapchain
creation, acquired frames, runtime capture timestamp progression, near/far
range metadata, observed depth cadence, average acquire CPU cost, and explicit
confidence-source reporting. The first headset visual is a simple acquisition
state color, not a false-color depth texture view.
