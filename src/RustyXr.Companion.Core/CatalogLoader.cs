using System.Text.Json;

namespace RustyXr.Companion.Core;

public sealed class CatalogLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<QuestSessionCatalog> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var catalog = await JsonSerializer.DeserializeAsync<QuestSessionCatalog>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return Normalize(catalog);
    }

    public async Task<CatalogAppSelection> SelectAppAsync(
        string path,
        string appId,
        CancellationToken cancellationToken = default)
    {
        var catalog = await LoadAsync(path, cancellationToken).ConfigureAwait(false);
        var app = catalog.Apps.FirstOrDefault(app => string.Equals(app.Id, appId, StringComparison.OrdinalIgnoreCase));
        if (app is null)
        {
            throw new InvalidOperationException($"Catalog app '{appId}' was not found.");
        }

        return new CatalogAppSelection(catalog, app, path, ResolveApkPath(path, app));
    }

    public static string? ResolveApkPath(string catalogPath, QuestAppTarget app)
    {
        if (string.IsNullOrWhiteSpace(app.ApkFile))
        {
            return null;
        }

        if (Uri.TryCreate(app.ApkFile, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return uri.LocalPath;
        }

        if (Uri.TryCreate(app.ApkFile, UriKind.Absolute, out uri) &&
            uri.Scheme is "http" or "https")
        {
            return uri.AbsoluteUri;
        }

        if (Path.IsPathRooted(app.ApkFile))
        {
            return Path.GetFullPath(app.ApkFile);
        }

        var catalogDirectory = Path.GetDirectoryName(Path.GetFullPath(catalogPath)) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(catalogDirectory, app.ApkFile));
    }

    public async Task SaveExampleAsync(string path, CancellationToken cancellationToken = default)
    {
        var catalog = new QuestSessionCatalog(
            QuestSessionCatalog.CurrentSchemaVersion,
            new[]
            {
                new QuestAppTarget(
                    "rusty-xr-quest-minimal",
                    "Rusty XR Minimal Quest APK",
                    "com.example.rustyxr.minimal",
                    ".MainActivity",
                    "../../../Rusty-XR/examples/quest-minimal-apk/build/outputs/rusty-xr-quest-minimal-debug.apk",
                    "Public Rust-native Android smoke-test APK. Build it locally; APK bytes are ignored and not committed."),
                new QuestAppTarget(
                    "rusty-xr-quest-composite-layer",
                    "Rusty XR Quest Composite Layer",
                    "com.example.rustyxr.composite",
                    ".CompositeLayerActivity",
                    "../../../Rusty-XR/examples/quest-composite-layer-apk/build/outputs/rusty-xr-quest-composite-layer-debug.apk",
                    "Public immersive Quest example with explicit synthetic, CPU diagnostic, GPU camera-buffer probe, projected stereo camera tiers, a generic diagnostic HUD, OSC listener profiles, and optional MediaProjection screen streaming."),
                new QuestAppTarget(
                    "example-user-apk",
                    "User supplied Quest APK",
                    "com.example.questapp",
                    null,
                    null,
                    "Placeholder target. Replace the package name or select an APK from the app.")
            },
            new[]
            {
                new DeviceProfile(
                    "balanced-dev",
                    "Balanced development",
                    new[]
                    {
                        new DeviceProperty("debug.oculus.cpuLevel", "2"),
                        new DeviceProperty("debug.oculus.gpuLevel", "2")
                    },
                    "Generic development profile for moderate thermal load."),
                new DeviceProfile(
                    "perf-smoke-test",
                    "Performance smoke test",
                    new[]
                    {
                        new DeviceProperty("debug.oculus.cpuLevel", "3"),
                        new DeviceProperty("debug.oculus.gpuLevel", "3")
                    },
                    "Short verification profile for launch and frame-callback checks."),
                new DeviceProfile(
                    "xr-composite-smoke-test",
                    "XR composite smoke test",
                    new[]
                    {
                        new DeviceProperty("debug.oculus.cpuLevel", "3"),
                        new DeviceProperty("debug.oculus.gpuLevel", "3")
                    },
                    "Short Quest verification profile for the immersive composite-layer example.")
            },
            new[]
            {
                new RuntimeProfile(
                    "minimal-contract-log",
                    "Minimal contract log",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-minimal-apk",
                        ["rustyxr.contracts"] = "synthetic"
                    },
                    "Documents that the minimal APK displays synthetic Rusty XR contract JSON and frame callback status."),
                new RuntimeProfile(
                    "synthetic-composite-layer",
                    "Synthetic layer smoke test",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "synthetic",
                        ["rustyxr.camera"] = "false",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.source"] = "synthetic",
                        ["rustyxr.depth"] = "off"
                    },
                    "Runs the OpenXR layer without camera or screen capture for lifecycle and renderer isolation."),
                new RuntimeProfile(
                    "osc-udp-listener",
                    "OSC UDP listener",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "synthetic",
                        ["rustyxr.camera"] = "false",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.source"] = "osc-udp",
                        ["rustyxr.depth"] = "off",
                        ["rustyxr.oscEnabled"] = "true",
                        ["rustyxr.diagnosticHudVisible"] = "true",
                        ["rustyxr.oscListenAddr"] = "0.0.0.0:9000",
                        ["rustyxr.oscMaxPacketBytes"] = "8192"
                    },
                    "Starts a generic OSC UDP listener in the headset app, draws a headset diagnostics panel inside the camera projection envelope, and logs received packet summaries for companion-to-headset transport checks."),
                new RuntimeProfile(
                    "osc-udp-listener-no-overlay",
                    "OSC UDP listener, no overlay",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "synthetic",
                        ["rustyxr.camera"] = "false",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.source"] = "osc-udp",
                        ["rustyxr.depth"] = "off",
                        ["rustyxr.oscEnabled"] = "true",
                        ["rustyxr.diagnosticHudVisible"] = "false",
                        ["rustyxr.oscOverlayEnabled"] = "false",
                        ["rustyxr.oscListenAddr"] = "0.0.0.0:9000",
                        ["rustyxr.oscMaxPacketBytes"] = "8192"
                    },
                    "Starts the same generic OSC UDP listener without drawing the headset diagnostics panel so OSC ingress cost can be measured separately from canvas rendering."),
                new RuntimeProfile(
                    "camera-source-diagnostics",
                    "Camera source diagnostics",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "camera-source-diagnostics",
                        ["rustyxr.camera"] = "true",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.source"] = "headset-camera",
                        ["rustyxr.depth"] = "off"
                    },
                    "Enumerates public Camera2 source capabilities and writes camera-source-diagnostics.json into the verification bundle when logcat capture is enabled."),
                new RuntimeProfile(
                    "environment-depth-diagnostics",
                    "Environment depth diagnostics",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "synthetic",
                        ["rustyxr.camera"] = "false",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.source"] = "environment-depth",
                        ["rustyxr.depth"] = "visualize",
                        ["rustyxr.depthHandRemoval"] = "false",
                        ["rustyxr.xrRenderScale"] = "0.75",
                        ["rustyxr.xrFixedFoveationLevel"] = "0"
                    },
                    "Starts the OpenXR environment-depth provider only for this diagnostic profile, logs depth resolution, near/far, runtime capture timestamps, acquire cost, update cadence, and confidence availability, and renders the current stereo depth texture in headset as a per-eye D16 grayscale diagnostic."),
                new RuntimeProfile(
                    "camera-diagnostic-cpu-copy",
                    "Camera diagnostic CPU copy",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "cpu-diagnostic-flat-copy",
                        ["rustyxr.camera"] = "true",
                        ["rustyxr.cameraWidth"] = "1280",
                        ["rustyxr.cameraHeight"] = "1280",
                        ["rustyxr.cameraPreferredSquare"] = "1280",
                        ["rustyxr.cameraMaxDimension"] = "1920",
                        ["rustyxr.cameraProjectionFovYDegrees"] = "92",
                        ["rustyxr.cameraPreviewFovYDegrees"] = "60",
                        ["rustyxr.cameraProjectionScale"] = "0.75",
                        ["rustyxr.cameraProjectionMode"] = "display-screen-homography",
                        ["rustyxr.cameraRawOverlayOverscan"] = "1.06",
                        ["rustyxr.cameraFullViewOverlayOverscan"] = "2.10",
                        ["rustyxr.cameraEdgeFade"] = "0.12",
                        ["rustyxr.cameraStereoLayout"] = "mono",
                        ["rustyxr.xrRenderScale"] = "0.75",
                        ["rustyxr.xrFixedFoveationLevel"] = "0",
                        ["rustyxr.cameraAllowCpuFallback"] = "true",
                        ["rustyxr.cameraCpuUploadHz"] = "4",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.source"] = "headset-camera",
                        ["rustyxr.depth"] = "off"
                    },
                    "Runs the OpenXR diagnostic flat camera copy from mono headset Camera2 frames with MediaProjection disabled for renderer isolation."),
                new RuntimeProfile(
                    "camera-gpu-buffer-probe",
                    "Camera GPU buffer probe",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "gpu-buffer-probe",
                        ["rustyxr.camera"] = "true",
                        ["rustyxr.cameraWidth"] = "1280",
                        ["rustyxr.cameraHeight"] = "1280",
                        ["rustyxr.cameraPreferredSquare"] = "1280",
                        ["rustyxr.cameraMaxDimension"] = "1920",
                        ["rustyxr.cameraProjectionFovYDegrees"] = "92",
                        ["rustyxr.cameraPreviewFovYDegrees"] = "60",
                        ["rustyxr.cameraProjectionScale"] = "0.75",
                        ["rustyxr.cameraRawOverlayOverscan"] = "1.06",
                        ["rustyxr.cameraFullViewOverlayOverscan"] = "2.10",
                        ["rustyxr.cameraEdgeFade"] = "0.12",
                        ["rustyxr.cameraStereoLayout"] = "separate",
                        ["rustyxr.xrRenderScale"] = "0.75",
                        ["rustyxr.xrFixedFoveationLevel"] = "0",
                        ["rustyxr.cameraAllowCpuFallback"] = "false",
                        ["rustyxr.cameraCpuUploadHz"] = "0",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.source"] = "headset-camera",
                        ["rustyxr.depth"] = "off"
                    },
                    "Requests Camera2 PRIVATE hardware buffers and validates GPU-buffer availability without claiming metadata-backed stereo alignment."),
                new RuntimeProfile(
                    "camera-stereo-gpu-composite",
                    "Camera stereo GPU composite",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "gpu-projected",
                        ["rustyxr.camera"] = "true",
                        ["rustyxr.cameraWidth"] = "1280",
                        ["rustyxr.cameraHeight"] = "1280",
                        ["rustyxr.cameraPreferredSquare"] = "1280",
                        ["rustyxr.cameraMaxDimension"] = "1920",
                        ["rustyxr.cameraProjectionFovYDegrees"] = "92",
                        ["rustyxr.cameraPreviewFovYDegrees"] = "60",
                        ["rustyxr.cameraProjectionScale"] = "0.75",
                        ["rustyxr.cameraRawOverlayOverscan"] = "1.06",
                        ["rustyxr.cameraFullViewOverlayOverscan"] = "2.10",
                        ["rustyxr.cameraEdgeFade"] = "0.12",
                        ["rustyxr.cameraTextureRotation"] = "rotate0",
                        ["rustyxr.cameraTextureFlipX"] = "false",
                        ["rustyxr.cameraTextureFlipY"] = "false",
                        ["rustyxr.cameraTextureMirror"] = "false",
                        ["rustyxr.cameraTextureTransformSource"] = "public-quest-visual-check",
                        ["rustyxr.cameraTextureTransformReason"] = "Quest Camera2 hardware-buffer UVs are projected with no post-projection texture flip; FlipY remains a diagnostic override for other devices or drivers",
                        ["rustyxr.leftCameraTextureRotation"] = "rotate0",
                        ["rustyxr.leftCameraTextureFlipX"] = "false",
                        ["rustyxr.leftCameraTextureFlipY"] = "false",
                        ["rustyxr.leftCameraTextureMirror"] = "false",
                        ["rustyxr.rightCameraTextureRotation"] = "rotate0",
                        ["rustyxr.rightCameraTextureFlipX"] = "false",
                        ["rustyxr.rightCameraTextureFlipY"] = "false",
                        ["rustyxr.rightCameraTextureMirror"] = "false",
                        ["rustyxr.cameraSourceEyeMapping"] = "left-right",
                        ["rustyxr.cameraOrientationDiagnosticMode"] = "off",
                        ["rustyxr.visualReleaseAccepted"] = "true",
                        ["rustyxr.visualAcceptanceToken"] = "manual-visual-accepted",
                        ["rustyxr.cameraStereoLayout"] = "separate",
                        ["rustyxr.cameraStereoPairMaxDeltaNs"] = "5000000",
                        ["rustyxr.xrRenderScale"] = "0.75",
                        ["rustyxr.xrFixedFoveationLevel"] = "0",
                        ["rustyxr.cameraAllowCpuFallback"] = "false",
                        ["rustyxr.cameraCpuUploadHz"] = "0",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.source"] = "headset-camera",
                        ["rustyxr.depth"] = "off"
                    },
                    "Final stereo profile. Verification fails unless one projection-status line reports paired left/right GPU buffers, platform or public estimated-profile pose, activeTier=gpu-projected, alignedProjection=true, and explicit manual visual acceptance."),
                new RuntimeProfile(
                    "camera-stereo-gpu-composite-quad-surface",
                    "Camera stereo GPU composite, quad-surface mode",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "gpu-projected",
                        ["rustyxr.camera"] = "true",
                        ["rustyxr.cameraWidth"] = "1280",
                        ["rustyxr.cameraHeight"] = "1280",
                        ["rustyxr.cameraPreferredSquare"] = "1280",
                        ["rustyxr.cameraMaxDimension"] = "1920",
                        ["rustyxr.cameraProjectionFovYDegrees"] = "92",
                        ["rustyxr.cameraPreviewFovYDegrees"] = "60",
                        ["rustyxr.cameraProjectionScale"] = "0.75",
                        ["rustyxr.cameraProjectionMode"] = "quad-surface",
                        ["rustyxr.cameraColorMode"] = "external-rgb",
                        ["rustyxr.cameraColorContrast"] = "1.1",
                        ["rustyxr.cameraColorBrightness"] = "0.04",
                        ["rustyxr.cameraColorSaturation"] = "1.0",
                        ["rustyxr.cameraRawOverlayOverscan"] = "1.06",
                        ["rustyxr.cameraFullViewOverlayOverscan"] = "2.15",
                        ["rustyxr.cameraEdgeFade"] = "0.10",
                        ["rustyxr.cameraTextureRotation"] = "rotate0",
                        ["rustyxr.cameraTextureFlipX"] = "false",
                        ["rustyxr.cameraTextureFlipY"] = "false",
                        ["rustyxr.cameraTextureMirror"] = "false",
                        ["rustyxr.cameraTextureTransformSource"] = "public-quest-visual-check",
                        ["rustyxr.cameraTextureTransformReason"] = "Quad-surface projection comparison keeps Camera2 hardware-buffer UVs unflipped after projection; FlipY remains a diagnostic override for other devices or drivers",
                        ["rustyxr.leftCameraTextureRotation"] = "rotate0",
                        ["rustyxr.leftCameraTextureFlipX"] = "false",
                        ["rustyxr.leftCameraTextureFlipY"] = "false",
                        ["rustyxr.leftCameraTextureMirror"] = "false",
                        ["rustyxr.rightCameraTextureRotation"] = "rotate0",
                        ["rustyxr.rightCameraTextureFlipX"] = "false",
                        ["rustyxr.rightCameraTextureFlipY"] = "false",
                        ["rustyxr.rightCameraTextureMirror"] = "false",
                        ["rustyxr.cameraSourceEyeMapping"] = "left-right",
                        ["rustyxr.cameraOrientationDiagnosticMode"] = "off",
                        ["rustyxr.visualReleaseAccepted"] = "false",
                        ["rustyxr.visualAcceptanceToken"] = "visual-check-open",
                        ["rustyxr.cameraStereoLayout"] = "separate",
                        ["rustyxr.cameraStereoPairMaxDeltaNs"] = "5000000",
                        ["rustyxr.xrRenderScale"] = "0.75",
                        ["rustyxr.xrFixedFoveationLevel"] = "0",
                        ["rustyxr.cameraAllowCpuFallback"] = "false",
                        ["rustyxr.cameraCpuUploadHz"] = "0",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.source"] = "headset-camera",
                        ["rustyxr.depth"] = "off"
                    },
                    "A/B projection profile. It keeps the same paired Camera2 GPU-buffer path and public border, reconstructs the content-surface UV that a head-anchored quad would rasterize, and leaves the visual release gate closed while operator testing compares projection geometry and color behavior against the default display-screen homography. This profile is not yet the final performance or color reference."),
                new RuntimeProfile(
                    "passthrough-underlay-hotload-neutral",
                    "Passthrough underlay hotload, neutral",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "synthetic",
                        ["rustyxr.camera"] = "false",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.openxrPassthroughProbe"] = "underlay",
                        ["rustyxr.passthroughStyleMode"] = "none",
                        ["rustyxr.passthroughOpacity"] = "1.0",
                        ["rustyxr.source"] = "platform-passthrough",
                        ["rustyxr.depth"] = "off"
                    },
                    "Launches a transparent OpenXR projection layer over native compositor passthrough so later catalog launches can hotload style values."),
                new RuntimeProfile(
                    "passthrough-underlay-hotload-lut-opponent",
                    "Passthrough underlay hotload, RGB LUT opponent",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "synthetic",
                        ["rustyxr.camera"] = "false",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.openxrPassthroughProbe"] = "underlay",
                        ["rustyxr.xrDisplayRefreshHz"] = "120.0",
                        ["rustyxr.passthroughStyleMode"] = "color-lut",
                        ["rustyxr.passthroughOpacity"] = "1.0",
                        ["rustyxr.passthroughLutResolution"] = "32",
                        ["rustyxr.passthroughLutWeight"] = "1.0",
                        ["rustyxr.passthroughLutFlickerHz"] = "0.0",
                        ["rustyxr.source"] = "platform-passthrough",
                        ["rustyxr.depth"] = "off"
                    },
                    "Live native compositor passthrough profile using XR_META_passthrough_color_lut with a smooth RGB opponent palette."),
                new RuntimeProfile(
                    "passthrough-underlay-hotload-lut-flicker-10hz",
                    "Passthrough underlay hotload, RGB LUT flicker 10 Hz",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "synthetic",
                        ["rustyxr.camera"] = "false",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.openxrPassthroughProbe"] = "underlay",
                        ["rustyxr.xrDisplayRefreshHz"] = "120.0",
                        ["rustyxr.passthroughStyleMode"] = "color-lut",
                        ["rustyxr.passthroughOpacity"] = "1.0",
                        ["rustyxr.passthroughLutResolution"] = "32",
                        ["rustyxr.passthroughLutWeight"] = "1.0",
                        ["rustyxr.passthroughLutFlickerHz"] = "10.0",
                        ["rustyxr.source"] = "platform-passthrough",
                        ["rustyxr.depth"] = "off"
                    },
                    "WARNING: intentional strobing profile. Device-side square-wave switch between an RGB LUT and its half-phase inverse at 10 full cycles per second, with a 120 Hz display-refresh request."),
                new RuntimeProfile(
                    "passthrough-underlay-hotload-lut-flicker-40hz",
                    "Passthrough underlay hotload, RGB LUT flicker 40 Hz",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "synthetic",
                        ["rustyxr.camera"] = "false",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.openxrPassthroughProbe"] = "underlay",
                        ["rustyxr.xrDisplayRefreshHz"] = "120.0",
                        ["rustyxr.passthroughStyleMode"] = "color-lut",
                        ["rustyxr.passthroughOpacity"] = "1.0",
                        ["rustyxr.passthroughLutResolution"] = "32",
                        ["rustyxr.passthroughLutWeight"] = "1.0",
                        ["rustyxr.passthroughLutFlickerHz"] = "40.0",
                        ["rustyxr.source"] = "platform-passthrough",
                        ["rustyxr.depth"] = "off"
                    },
                    "WARNING: intentional strobing profile. Device-side square-wave switch between an RGB LUT and its half-phase inverse at 40 full cycles per second. At 120 Hz this is frame-quantized because each half-cycle averages 1.5 display frames."),
                new RuntimeProfile(
                    "passthrough-underlay-hotload-lut-flicker-60hz",
                    "Passthrough underlay hotload, RGB LUT flicker 60 Hz",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "synthetic",
                        ["rustyxr.camera"] = "false",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.openxrPassthroughProbe"] = "underlay",
                        ["rustyxr.xrDisplayRefreshHz"] = "120.0",
                        ["rustyxr.passthroughStyleMode"] = "color-lut",
                        ["rustyxr.passthroughOpacity"] = "1.0",
                        ["rustyxr.passthroughLutResolution"] = "32",
                        ["rustyxr.passthroughLutWeight"] = "1.0",
                        ["rustyxr.passthroughLutFlickerHz"] = "60.0",
                        ["rustyxr.source"] = "platform-passthrough",
                        ["rustyxr.depth"] = "off"
                    },
                    "WARNING: intentional strobing profile. Device-side square-wave switch between an RGB LUT and its half-phase inverse at 60 full cycles per second. This requires 120 state transitions per second, so it aliases unless the runtime presents at 120 Hz and accepts per-frame style updates."),
                new RuntimeProfile(
                    "full-field-red-black-flicker-10hz",
                    "Full-field red/black flicker 10 Hz",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "synthetic",
                        ["rustyxr.camera"] = "false",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.openxrPassthroughProbe"] = "off",
                        ["rustyxr.xrDisplayRefreshHz"] = "120.0",
                        ["rustyxr.fullFieldFlickerHz"] = "10.0",
                        ["rustyxr.source"] = "rendered-full-field",
                        ["rustyxr.depth"] = "off"
                    },
                    "WARNING: intentional full-field strobing profile. OpenXR projection-layer clear-color flicker between bright red and black at 10 full cycles per second with a 120 Hz display-refresh request."),
                new RuntimeProfile(
                    "full-field-red-black-flicker-40hz",
                    "Full-field red/black flicker 40 Hz",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "synthetic",
                        ["rustyxr.camera"] = "false",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.openxrPassthroughProbe"] = "off",
                        ["rustyxr.xrDisplayRefreshHz"] = "120.0",
                        ["rustyxr.fullFieldFlickerHz"] = "40.0",
                        ["rustyxr.source"] = "rendered-full-field",
                        ["rustyxr.depth"] = "off"
                    },
                    "WARNING: intentional full-field strobing profile. OpenXR projection-layer clear-color flicker between bright red and black at 40 full cycles per second with a 120 Hz display-refresh request; half-cycles are frame-quantized."),
                new RuntimeProfile(
                    "full-field-red-black-flicker-60hz",
                    "Full-field red/black flicker 60 Hz",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "synthetic",
                        ["rustyxr.camera"] = "false",
                        ["rustyxr.mediaProjection"] = "false",
                        ["rustyxr.openxrPassthroughProbe"] = "off",
                        ["rustyxr.xrDisplayRefreshHz"] = "120.0",
                        ["rustyxr.fullFieldFlickerHz"] = "60.0",
                        ["rustyxr.source"] = "rendered-full-field",
                        ["rustyxr.depth"] = "off"
                    },
                    "WARNING: intentional full-field strobing profile. OpenXR projection-layer clear-color flicker between bright red and black at 60 full cycles per second; this requires 120 rendered frames per second and one state change per displayed frame."),
                new RuntimeProfile(
                    "media-projection-stream",
                    "MediaProjection stream",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-composite-layer-apk",
                        ["rustyxr.cameraTier"] = "cpu-diagnostic-flat-copy",
                        ["rustyxr.camera"] = "true",
                        ["rustyxr.cameraWidth"] = "1280",
                        ["rustyxr.cameraHeight"] = "1280",
                        ["rustyxr.cameraPreferredSquare"] = "1280",
                        ["rustyxr.cameraMaxDimension"] = "1920",
                        ["rustyxr.cameraProjectionFovYDegrees"] = "92",
                        ["rustyxr.cameraPreviewFovYDegrees"] = "60",
                        ["rustyxr.cameraProjectionScale"] = "0.75",
                        ["rustyxr.cameraRawOverlayOverscan"] = "1.06",
                        ["rustyxr.cameraFullViewOverlayOverscan"] = "2.10",
                        ["rustyxr.cameraEdgeFade"] = "0.12",
                        ["rustyxr.cameraStereoLayout"] = "mono",
                        ["rustyxr.xrRenderScale"] = "0.75",
                        ["rustyxr.xrFixedFoveationLevel"] = "0",
                        ["rustyxr.cameraAllowCpuFallback"] = "true",
                        ["rustyxr.cameraCpuUploadHz"] = "4",
                        ["rustyxr.mediaProjection"] = "true",
                        ["rustyxr.mediaProjectionDelayMs"] = "5000",
                        ["rustyxr.source"] = "headset-camera",
                        ["rustyxr.stream"] = "display-composite-rgba"
                    },
                    "Runs the camera-driven layer and requests MediaProjection consent only to stream the final screen to a Windows receiver."),
                new RuntimeProfile(
                    "example-runtime",
                    "Example runtime profile",
                    new Dictionary<string, string>
                    {
                        ["example.enabled"] = "true"
                    },
                    "Placeholder profile for apps that consume a target-specific config file.")
            });

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, catalog, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static QuestSessionCatalog Normalize(QuestSessionCatalog? catalog)
    {
        if (catalog is null)
        {
            return new QuestSessionCatalog(
                QuestSessionCatalog.CurrentSchemaVersion,
                Array.Empty<QuestAppTarget>(),
                Array.Empty<DeviceProfile>(),
                Array.Empty<RuntimeProfile>());
        }

        return catalog with
        {
            SchemaVersion = string.IsNullOrWhiteSpace(catalog.SchemaVersion)
                ? QuestSessionCatalog.CurrentSchemaVersion
                : catalog.SchemaVersion
        };
    }
}
