namespace RustyXr.Companion.Core;

public static class SourceWorkspaceGuide
{
    public const string RustyXrRepoName = "Rusty-XR";
    public const string CompanionRepoName = "Rusty-XR-Companion-Apps";
    public const string MorphospaceGuiRepoName = "rusty-gui";
    public const string MorphospaceMakepadRepoName = "rusty-makepad";
    public const string MorphospaceQuestRepoName = "rusty-quest";
    public const string MorphospaceQuestMakepadRepoName = "rusty-quest-makepad";
    public const string MorphospaceMakepadForkRepoName = "makepad-morphospace";
    public const string MorphospaceHostessRepoName = "rusty-hostess";

    public static SourceWorkspaceStatus Evaluate(string? workspaceRoot = null, string? currentDirectory = null)
    {
        var root = ResolveWorkspaceRoot(workspaceRoot, currentDirectory ?? Directory.GetCurrentDirectory());
        var companionPath = Path.Combine(root, CompanionRepoName);
        var rustyXrPath = Path.Combine(root, RustyXrRepoName);
        var morphospaceGuiPath = Path.Combine(root, MorphospaceGuiRepoName);
        var morphospaceMakepadPath = Path.Combine(root, MorphospaceMakepadRepoName);
        var morphospaceQuestPath = Path.Combine(root, MorphospaceQuestRepoName);
        var morphospaceQuestMakepadPath = Path.Combine(root, MorphospaceQuestMakepadRepoName);
        var morphospaceMakepadForkPath = Path.Combine(root, MorphospaceMakepadForkRepoName);
        var morphospaceHostessPath = Path.Combine(root, MorphospaceHostessRepoName);

        var minimalBuildScript = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-minimal-apk",
            "tools",
            "Build-QuestMinimalApk.ps1");
        var minimalCatalog = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-minimal-apk",
            "catalog",
            "rusty-xr-quest-minimal.catalog.json");
        var minimalApk = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-minimal-apk",
            "build",
            "outputs",
            "rusty-xr-quest-minimal-debug.apk");
        var compositeBuildScript = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-composite-layer-apk",
            "tools",
            "Build-QuestCompositeLayerApk.ps1");
        var compositeCatalog = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-composite-layer-apk",
            "catalog",
            "rusty-xr-quest-composite-layer.catalog.json");
        var compositeApk = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-composite-layer-apk",
            "build",
            "outputs",
            CompanionContentLayout.CompositeQuestApkFileName);
        var brokerBuildScript = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-broker-apk",
            "tools",
            "Build-QuestBrokerApk.ps1");
        var brokerCatalog = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-broker-apk",
            "catalog",
            "rusty-xr-quest-broker.catalog.json");
        var brokerApk = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-broker-apk",
            "build",
            "outputs",
            "rusty-xr-quest-broker-debug.apk");
        var companionCliProject = Path.Combine(
            companionPath,
            "src",
            "RustyXr.Companion.Cli",
            "RustyXr.Companion.Cli.csproj");
        var optionalMorphospaceRepos = new[]
        {
            RepositoryStatus(
                MorphospaceGuiRepoName,
                morphospaceGuiPath,
                "Portable GUI descriptors and widget contracts."),
            RepositoryStatus(
                MorphospaceMakepadRepoName,
                morphospaceMakepadPath,
                "Generic Makepad adapters and canonical Makepad app settings/profile resolver."),
            RepositoryStatus(
                MorphospaceQuestRepoName,
                morphospaceQuestPath,
                "Quest platform profiles, launch property write plans, and device/runtime policy."),
            RepositoryStatus(
                MorphospaceQuestMakepadRepoName,
                morphospaceQuestMakepadPath,
                "Quest-specific Makepad app adapters, including camera-shell and mesh-replay bundles."),
            RepositoryStatus(
                MorphospaceMakepadForkRepoName,
                morphospaceMakepadForkPath,
                "Maintained Morphospace Makepad fork checkout used by Makepad app-shell builds."),
            RepositoryStatus(
                MorphospaceHostessRepoName,
                morphospaceHostessPath,
                "Hostess app shell that consumes generated Makepad effective-settings reports.")
        };

        var commands = new[]
        {
            new SourceWorkspaceCommand(
                "companion-tooling",
                "Install or update the companion-managed Quest operator tools.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- tooling install-official"),
            new SourceWorkspaceCommand(
                "companion-media-runtime",
                "Install or update the optional companion-managed FFmpeg media runtime.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- tooling install-media"),
            new SourceWorkspaceCommand(
                "devices",
                "Confirm ADB sees a trusted Quest.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- devices"),
            new SourceWorkspaceCommand(
                "validate-morphospace-gui",
                "Validate the optional Rusty GUI repo that owns portable UI descriptors used by Makepad adapters.",
                morphospaceGuiPath,
                @"powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_all.ps1"),
            new SourceWorkspaceCommand(
                "validate-morphospace-makepad-settings",
                "Validate the optional Rusty Makepad repo that owns canonical settings, profiles, provenance, and hotload decisions.",
                morphospaceMakepadPath,
                @"powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_all.ps1"),
            new SourceWorkspaceCommand(
                "validate-morphospace-quest-profiles",
                "Validate the optional Rusty Quest repo that owns platform profile and Android property write-plan behavior.",
                morphospaceQuestPath,
                @"powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_all.ps1"),
            new SourceWorkspaceCommand(
                "validate-morphospace-quest-makepad",
                "Validate the optional Rusty Quest Makepad repo that owns Quest-specific Makepad app adapters.",
                morphospaceQuestMakepadPath,
                @"powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\check_all.ps1"),
            new SourceWorkspaceCommand(
                "validate-morphospace-hostess-makepad",
                "Validate the optional Hostess Makepad app-shell consumer.",
                morphospaceHostessPath,
                @"cargo check --manifest-path apps\hostess-t-makepad\Cargo.toml"),
            new SourceWorkspaceCommand(
                "resolve-morphospace-quest-makepad-effective-settings",
                "Resolve the Quest Makepad camera-shell profile into one canonical effective-settings report with provenance.",
                morphospaceMakepadPath,
                @"New-Item -ItemType Directory -Force -Path .\local-artifacts | Out-Null; cargo run -p rusty-makepad-settings-cli -- resolve --surface ..\rusty-quest-makepad\fixtures\settings\quest-makepad-camera-shell.settings.json --profile ..\rusty-quest-makepad\fixtures\profiles\mesh-replay.settings-profile.json --out .\local-artifacts\quest-makepad-effective-settings.json"),
            new SourceWorkspaceCommand(
                "run-hostess-makepad-effective-settings",
                "Run the Hostess Makepad shell from the generated effective-settings report instead of hand-authored ADB launch extras.",
                morphospaceHostessPath,
                @"cargo run --manifest-path apps\hostess-t-makepad\Cargo.toml -- --makepad-effective-settings ..\rusty-makepad\local-artifacts\quest-makepad-effective-settings.json --makepad-effective-settings-receipt-out .\target\hostess-makepad-effective-settings-receipt.json"),
            new SourceWorkspaceCommand(
                "build-minimal-apk",
                "Build the public Rusty XR minimal Android smoke-test APK.",
                rustyXrPath,
                @"powershell -ExecutionPolicy Bypass -File .\examples\quest-minimal-apk\tools\Build-QuestMinimalApk.ps1"),
            new SourceWorkspaceCommand(
                "verify-minimal-apk",
                "Install, launch, and capture a verification bundle for the minimal APK.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-minimal-apk\catalog\rusty-xr-quest-minimal.catalog.json --app rusty-xr-quest-minimal --serial <serial> --install --launch --device-profile perf-smoke-test --runtime-profile minimal-contract-log --settle-ms 4000 --out .\artifacts\verify"),
            new SourceWorkspaceCommand(
                "build-composite-apk",
                "Build the public Rusty XR immersive Quest composite-layer APK.",
                rustyXrPath,
                @"powershell -ExecutionPolicy Bypass -File .\examples\quest-composite-layer-apk\tools\Build-QuestCompositeLayerApk.ps1 -OpenXrLoaderPath <path-to-libopenxr_loader.so>"),
            new SourceWorkspaceCommand(
                "verify-composite-apk",
                "Install, launch, log, and capture diagnostics for the composite-layer APK.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-composite-layer-apk\catalog\rusty-xr-quest-composite-layer.catalog.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile camera-stereo-gpu-composite --settle-ms 9000 --logcat-lines 1400 --out .\artifacts\verify"),
            new SourceWorkspaceCommand(
                "verify-composite-fast075",
                "Install, launch, log, and capture the fast 0.75 direct Camera2 stereo projection profile.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-composite-layer-apk\catalog\rusty-xr-quest-composite-layer.catalog.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-comparison-level-4 --runtime-profile camera-stereo-gpu-composite-fast075 --settle-ms 9000 --logcat-lines 1600 --out .\artifacts\verify"),
            new SourceWorkspaceCommand(
                "verify-osc-listener",
                "Install, launch, and log the generic OSC UDP listener profile.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-composite-layer-apk\catalog\rusty-xr-quest-composite-layer.catalog.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile osc-udp-listener --settle-ms 5000 --logcat-lines 1000 --out .\artifacts\verify"),
            new SourceWorkspaceCommand(
                "build-broker-apk",
                "Build the public Rusty XR Quest broker proof-of-concept APK.",
                rustyXrPath,
                @"powershell -ExecutionPolicy Bypass -File .\examples\quest-broker-apk\tools\Build-QuestBrokerApk.ps1"),
            new SourceWorkspaceCommand(
                "verify-broker-apk",
                "Install, launch, and log the localhost broker status/WebSocket proof.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-broker-apk\catalog\rusty-xr-quest-broker.catalog.json --app rusty-xr-quest-broker --serial <serial> --stop-catalog-apps --install --launch --device-profile broker-smoke-test --runtime-profile broker-latency-websocket-lsl --settle-ms 5000 --logcat-lines 1000 --out .\artifacts\verify"),
            new SourceWorkspaceCommand(
                "open-broker-system-console",
                "Launch the visible broker console directly on the System page.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog launch --path ..\Rusty-XR\examples\quest-broker-apk\catalog\rusty-xr-quest-broker.catalog.json --app rusty-xr-quest-broker-console --serial <serial> --runtime-profile broker-console-system-page"),
            new SourceWorkspaceCommand(
                "verify-broker-osc-ingress",
                "Install, launch, and log the broker OSC drive ingress profile.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-broker-apk\catalog\rusty-xr-quest-broker.catalog.json --app rusty-xr-quest-broker --serial <serial> --stop-catalog-apps --install --launch --device-profile broker-smoke-test --runtime-profile broker-osc-drive-ingress --settle-ms 5000 --logcat-lines 1000 --out .\artifacts\verify"),
            new SourceWorkspaceCommand(
                "inspect-broker-host-manifest",
                "Read the broker-described host role, endpoints, security policy, clock domain, and capabilities.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- broker host-manifest --json"),
            new SourceWorkspaceCommand(
                "start-broker-shell-helper",
                "Build, push, and launch the optional ADB shell helper, then read broker shell-helper status.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- broker shell-helper start --serial <serial> --rusty-xr-root ..\Rusty-XR --probe-codecs --emit-synthetic-video-metadata --json"),
            new SourceWorkspaceCommand(
                "probe-broker-shell-helper-cameras",
                "Build, push, launch, and report bounded shell-visible camera metadata plus Camera2 open/capture feasibility.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- broker shell-helper start --serial <serial> --rusty-xr-root ..\Rusty-XR --probe-cameras --probe-camera-open --json"),
            new SourceWorkspaceCommand(
                "probe-broker-shell-helper-binary",
                "Build, push, launch, forward, and validate the optional ADB shell helper synthetic binary video side channel.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- broker shell-helper binary-probe --serial <serial> --rusty-xr-root ..\Rusty-XR --json"),
            new SourceWorkspaceCommand(
                "probe-broker-shell-helper-mediacodec",
                "Build, push, launch, forward, and validate the optional ADB shell helper MediaCodec synthetic-Surface side channel.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- broker shell-helper binary-probe --serial <serial> --rusty-xr-root ..\Rusty-XR --mediacodec-synthetic --encoded-video-frames 4 --encoded-video-width 320 --encoded-video-height 180 --json"),
            new SourceWorkspaceCommand(
                "probe-broker-shell-helper-screenrecord",
                "Build, push, launch, forward, validate, and save the optional ADB shell helper screenrecord H.264 display side channel.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- broker shell-helper binary-probe --serial <serial> --rusty-xr-root ..\Rusty-XR --screenrecord-source --encoded-video-width 320 --encoded-video-height 180 --encoded-video-bitrate 500000 --screenrecord-time-limit 1 --payload-out .\artifacts\broker-shell-helper\screenrecord.h264 --json"),
            new SourceWorkspaceCommand(
                "start-broker-shell-helper-proximity-watchdog",
                "Start the optional ADB shell helper awake/proximity watchdog for autonomous Quest sessions.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- broker shell-helper start --serial <serial> --rusty-xr-root ..\Rusty-XR --no-broker-report --skip-status --proximity-watchdog --proximity-watchdog-until-stopped --proximity-watchdog-ensure-stay-awake --json"),
            new SourceWorkspaceCommand(
                "probe-broker-app-camera-luma",
                "Forward, start, receive, and save a bounded broker app-context Camera2 raw-luma side-channel probe.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- broker app-camera-luma-probe --serial <serial> --camera-id <id> --frame-count 2 --payload-out .\artifacts\broker-app-camera\luma.raw --json"),
            new SourceWorkspaceCommand(
                "inspect-broker-shell-helper-h264",
                "Inspect a saved broker shell-helper H.264 artifact and optionally add --decode --ffmpeg <path> for an external decoder probe.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- media inspect-h264 --payload .\artifacts\broker-shell-helper\screenrecord.h264 --json"),
            new SourceWorkspaceCommand(
                "inspect-broker-app-camera-luma",
                "Inspect a saved broker app-context raw-luma artifact and optionally write a local PGM contact sheet.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- media inspect-raw-luma --payload .\artifacts\broker-app-camera\luma.raw --width 720 --height 480 --contact-sheet .\artifacts\broker-app-camera\luma.pgm --json"),
            new SourceWorkspaceCommand(
                "probe-broker-app-camera-h264",
                "Forward, start, receive, and save a bounded broker app-context Camera2-to-H.264 side-channel probe.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- broker app-camera-h264-probe --serial <serial> --camera-id <id> --capture-ms 900 --max-packets 12 --payload-out .\artifacts\broker-app-camera\camera.h264 --json"),
            new SourceWorkspaceCommand(
                "probe-broker-app-camera-h264-decode",
                "Run the broker-local Android MediaCodec H.264 decode-consumption probe for the app-camera side channel.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- broker app-camera-h264-decode-probe --serial <serial> --camera-id <id> --capture-ms 900 --max-packets 12 --json"),
            new SourceWorkspaceCommand(
                "launch-composite-broker-h264-consumer",
                "Launch the composite-layer APK with camera disabled and the broker H.264 consumer SurfaceTexture probe enabled.",
                companionPath,
                @"adb shell am start -a android.intent.action.MAIN -c com.oculus.intent.category.VR -n com.example.rustyxr.composite/.CompositeLayerActivity --ez rustyxr.camera false --ez rustyxr.mediaProjection false --ez rustyxr.brokerH264Consumer true --es rustyxr.brokerH264CameraId <id> --ei rustyxr.brokerH264Width 720 --ei rustyxr.brokerH264Height 480 --ei rustyxr.brokerH264CaptureMs 900 --ei rustyxr.brokerH264MaxPackets 12 --es rustyxr.brokerH264DecodeOutputMode surface-texture"),
            new SourceWorkspaceCommand(
                "launch-composite-broker-h264-openxr-layer",
                "Launch the composite-layer APK with broker H.264 decoded into hardware buffers, tagged with broker Camera2 projection metadata when available, and drawn by the OpenXR GPU-buffer path.",
                companionPath,
                @"adb shell am start -a android.intent.action.MAIN -c com.oculus.intent.category.VR -n com.example.rustyxr.composite/.CompositeLayerActivity --ez rustyxr.camera false --es rustyxr.cameraTier gpu-buffer-probe --ez rustyxr.cameraAllowCpuFallback false --ei rustyxr.cameraCpuUploadHz 0 --ez rustyxr.mediaProjection false --ez rustyxr.brokerH264Consumer true --es rustyxr.brokerH264CameraId <id> --ei rustyxr.brokerH264Width 720 --ei rustyxr.brokerH264Height 480 --ei rustyxr.brokerH264CaptureMs 900 --ei rustyxr.brokerH264MaxPackets 12 --es rustyxr.brokerH264DecodeOutputMode hardware-buffer"),
            new SourceWorkspaceCommand(
                "launch-composite-broker-h264-stereo-projection",
                "Launch the composite-layer APK with two broker H.264 streams from Quest Camera2 IDs 50 and 51 decoded into paired hardware buffers and handed to the OpenXR stereo projection path.",
                companionPath,
                @"adb shell am start -a android.intent.action.MAIN -c com.oculus.intent.category.VR -n com.example.rustyxr.composite/.CompositeLayerActivity --ez rustyxr.camera false --es rustyxr.cameraTier gpu-projected --es rustyxr.cameraStereoLayout separate --ez rustyxr.cameraAllowCpuFallback false --ei rustyxr.cameraCpuUploadHz 0 --ez rustyxr.mediaProjection false --ez rustyxr.brokerH264Consumer true --ez rustyxr.brokerH264Stereo true --es rustyxr.brokerH264LeftCameraId 50 --es rustyxr.brokerH264RightCameraId 51 --ei rustyxr.brokerH264StreamPort 8879 --ei rustyxr.brokerH264RightStreamPort 8880 --ei rustyxr.brokerH264Width 720 --ei rustyxr.brokerH264Height 480 --ei rustyxr.brokerH264CaptureMs 900 --ei rustyxr.brokerH264MaxPackets 12 --es rustyxr.brokerH264DecodeOutputMode hardware-buffer --es rustyxr.cameraTextureTransformSource public-broker-h264-stereo-visual-check --es rustyxr.cameraTextureTransformReason visual-check --es rustyxr.leftCameraTextureTransformSource public-broker-h264-left-visual-check --es rustyxr.leftCameraTextureTransformReason visual-check --es rustyxr.rightCameraTextureTransformSource public-broker-h264-right-visual-check --es rustyxr.rightCameraTextureTransformReason visual-check --ez rustyxr.visualReleaseAccepted false"),
            new SourceWorkspaceCommand(
                "launch-composite-broker-h264-live-stereo-projection",
                "Launch the composite-layer APK with the fast 0.75 broker H.264 stereo renderer-parity path: camera IDs 50/51, 1280 square frames, frame-order pairing, hardware-buffer decode, and fast raw projection.",
                companionPath,
                @"adb shell am start -a android.intent.action.MAIN -c com.oculus.intent.category.VR -n com.example.rustyxr.composite/.CompositeLayerActivity --ez rustyxr.camera false --es rustyxr.cameraTier gpu-projected --es rustyxr.cameraStereoLayout separate --ez rustyxr.cameraAllowCpuFallback false --ei rustyxr.cameraCpuUploadHz 0 --ez rustyxr.mediaProjection false --ez rustyxr.brokerH264Consumer true --ez rustyxr.brokerH264Stereo true --ez rustyxr.brokerH264LiveStream true --ez rustyxr.brokerH264LiveDecode true --es rustyxr.brokerH264LeftCameraId 50 --es rustyxr.brokerH264RightCameraId 51 --ei rustyxr.brokerH264StreamPort 8879 --ei rustyxr.brokerH264RightStreamPort 8880 --ei rustyxr.brokerH264Width 1280 --ei rustyxr.brokerH264Height 1280 --ei rustyxr.brokerH264CaptureMs 15000 --ei rustyxr.brokerH264MaxPackets 120 --ei rustyxr.brokerH264BitrateBps 6000000 --ei rustyxr.brokerH264StreamTimeoutMs 30000 --ei rustyxr.brokerH264DecodeTimeoutMs 20000 --es rustyxr.brokerH264DecodeOutputMode hardware-buffer --es rustyxr.brokerH264StereoPairingMode frame-order --ei rustyxr.brokerH264LivePairQueueLimit 8 --ef rustyxr.xrRenderScale 0.75 --ei rustyxr.xrFixedFoveationLevel 0 --ef rustyxr.cameraProjectionFovYDegrees 92 --ef rustyxr.cameraPreviewFovYDegrees 60 --ef rustyxr.cameraProjectionScale 0.75 --es rustyxr.cameraPipelinePreset raw-projection-fast-unorm --es rustyxr.cameraProjectionEffectMode raw-projection-fast --es rustyxr.cameraColorMode external-rgb --es rustyxr.cameraTextureTransformSource public-broker-h264-live-stereo-fast075-visual-check --es rustyxr.cameraTextureTransformReason fast075-renderer-parity --es rustyxr.leftCameraTextureTransformSource public-broker-h264-live-fast075-left-visual-check --es rustyxr.leftCameraTextureTransformReason visual-check --es rustyxr.rightCameraTextureTransformSource public-broker-h264-live-fast075-right-visual-check --es rustyxr.rightCameraTextureTransformReason visual-check --ez rustyxr.visualReleaseAccepted true"),
            new SourceWorkspaceCommand(
                "inspect-broker-app-camera-h264",
                "Inspect a saved broker app-context H.264 artifact and optionally add --decode --ffmpeg <path> for an external decoder probe.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- media inspect-h264 --payload .\artifacts\broker-app-camera\camera.h264 --json"),
            new SourceWorkspaceCommand(
                "decode-broker-app-camera-h264-preview",
                "Decode the first saved broker app-context H.264 frame to a PNG preview using an external FFmpeg sidecar.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- media decode-h264-preview --payload .\artifacts\broker-app-camera\camera.h264 --out .\artifacts\broker-app-camera\camera-preview.png --json"),
            new SourceWorkspaceCommand(
                "stop-broker-shell-helper",
                "Run the helper in disconnect-report mode, request any shell-side proximity watchdog to stop, and record disconnected state.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- broker shell-helper stop --serial <serial> --rusty-xr-root ..\Rusty-XR --no-build --json"),
            new SourceWorkspaceCommand(
                "send-broker-osc-drive",
                "Send one OSC drive value to a broker running on the Quest LAN IP.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- osc send --host <quest-lan-ip> --port 9000 --address /rusty-xr/drive/radius --arg float:0.75"),
            new SourceWorkspaceCommand(
                "compare-broker-osc-routes",
                "Write a clock-aligned direct target-app OSC versus broker OSC/WebSocket comparison bundle.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- broker compare --quest-host <quest-lan-ip> --serial <serial> --count 16 --interval-ms 250 --out .\artifacts\broker-compare --json"),
            new SourceWorkspaceCommand(
                "verify-lsl-runtime",
                "Check that the companion can load a user-supplied Windows lsl.dll.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- lsl runtime --lsl-dll <path-to-windows-lsl.dll> --json"),
            new SourceWorkspaceCommand(
                "run-lsl-loopback",
                "Write a local LSL loopback diagnostics bundle with JSON, CSV, Markdown, and PDF outputs.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- lsl loopback --lsl-dll <path-to-windows-lsl.dll> --count 16 --interval-ms 100 --out .\artifacts\lsl-loopback --json"),
            new SourceWorkspaceCommand(
                "run-broker-lsl-roundtrip",
                "Compare broker WebSocket latency samples against the broker-forwarded LSL stream.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- lsl broker-roundtrip --serial <serial> --lsl-dll <path-to-windows-lsl.dll> --count 8 --interval-ms 250 --out .\artifacts\lsl-broker --json"),
            new SourceWorkspaceCommand(
                "verify-environment-depth",
                "Install, launch, and validate OpenXR environment-depth acquisition diagnostics.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-composite-layer-apk\catalog\rusty-xr-quest-composite-layer.catalog.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile environment-depth-diagnostics --settle-ms 9000 --logcat-lines 1400 --out .\artifacts\verify"),
            new SourceWorkspaceCommand(
                "verify-environment-depth-mesh-overlay",
                "Install, launch, and validate the local-space OpenXR environment-depth mesh overlay.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-composite-layer-apk\catalog\rusty-xr-quest-composite-layer.catalog.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile environment-depth-mesh-overlay --settle-ms 9000 --logcat-lines 1600 --out .\artifacts\verify"),
            new SourceWorkspaceCommand(
                "verify-environment-depth-particle-overlay",
                "Install, launch, and validate the retained local-space OpenXR environment-depth particle overlay.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-composite-layer-apk\catalog\rusty-xr-quest-composite-layer.catalog.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile environment-depth-particle-overlay --settle-ms 9000 --logcat-lines 1600 --out .\artifacts\verify"),
            new SourceWorkspaceCommand(
                "verify-meta-hand-mesh-particles",
                "Install, launch, and validate Meta/OpenXR hand-mesh particle rendering.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-composite-layer-apk\catalog\rusty-xr-quest-composite-layer.catalog.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile meta-hand-mesh-particles --settle-ms 9000 --logcat-lines 1600 --out .\artifacts\verify")
        };

        return new SourceWorkspaceStatus(
            root,
            rustyXrPath,
            companionPath,
            File.Exists(Path.Combine(rustyXrPath, "Cargo.toml")),
            File.Exists(Path.Combine(companionPath, "RustyXr.Companion.slnx")),
            companionCliProject,
            minimalBuildScript,
            minimalCatalog,
            minimalApk,
            File.Exists(minimalApk),
            compositeBuildScript,
            compositeCatalog,
            compositeApk,
            File.Exists(compositeApk),
            brokerBuildScript,
            brokerCatalog,
            brokerApk,
            File.Exists(brokerApk),
            new[]
            {
                "Keep both public repos as siblings under one workspace folder.",
                "Treat the optional Morphospace Makepad repos as the active Makepad/settings/profile maintenance lanes when they are present.",
                "Resolve active Makepad behavior through rusty.gui.makepad.effective_settings.v1 reports before launching Hostess or Quest Makepad shells.",
                "Keep public Rusty XR Makepad examples as compatibility/reference evidence, not the settings authority for new Makepad app behavior.",
                "Use the companion-managed tooling cache for adb, Meta VR CLI / hzdb compatibility, scrcpy, and optional FFmpeg media decode tooling.",
                "Install Rust and Android build tooling only on machines that build APKs from source.",
                "Build APK bytes under Rusty XR ignored build folders, then verify them through catalog commands.",
                "Keep diagnostics, screenshots, media frames, APKs, signing material, and local caches out of git."
            },
            optionalMorphospaceRepos,
            commands);
    }

    public static string ToMarkdown(SourceWorkspaceStatus status)
    {
        var lines = new List<string>
        {
            "# Rusty XR Source Workspace",
            string.Empty,
            $"Workspace root: `{status.WorkspaceRoot}`",
            $"Rusty XR repo: `{status.RustyXrRepoPath}` ({FoundLabel(status.RustyXrRepoPresent)})",
            $"Companion repo: `{status.CompanionRepoPath}` ({FoundLabel(status.CompanionRepoPresent)})",
            string.Empty,
            "## Recommended Layout",
            string.Empty,
            "```text",
            @"<workspace>\Rusty-XR",
            @"<workspace>\Rusty-XR-Companion-Apps",
            "```",
            string.Empty,
            "## Optional Morphospace Makepad Layout",
            string.Empty,
            "These sibling repos are optional for public Rusty XR Companion users. When present, they are the active Morphospace Makepad maintenance lanes; the public Rusty XR Makepad example remains compatibility/reference evidence.",
            string.Empty,
            "```text",
            @"<workspace>\rusty-gui",
            @"<workspace>\rusty-makepad",
            @"<workspace>\rusty-quest",
            @"<workspace>\rusty-quest-makepad",
            @"<workspace>\makepad-morphospace",
            @"<workspace>\rusty-hostess",
            "```",
            string.Empty,
            "## Optional Morphospace Repo Status",
            string.Empty
        };

        lines.AddRange(status.OptionalMorphospaceRepos.Select(repo =>
            $"- `{repo.Name}`: `{repo.Path}` ({FoundLabel(repo.Present)}) - {repo.Role}"));

        lines.AddRange(new[]
        {
            string.Empty,
            "## Active Makepad Settings Flow",
            string.Empty,
            "- `rusty-makepad` owns the canonical settings surface, profiles, resolver, provenance, and hotload decision reports.",
            "- `rusty-quest-makepad` owns Quest-specific Makepad profile bundles and camera-shell adapters over that surface.",
            "- `rusty-quest` owns platform property write/readback plans; it is a transport layer, not a duplicate settings authority.",
            "- `rusty-hostess` consumes generated `rusty.gui.makepad.effective_settings.v1` reports with `--makepad-effective-settings` or the matching environment variables.",
            "- Use `resolve-morphospace-quest-makepad-effective-settings` before changing app behavior; avoid hand-authoring per-launch ADB extras for active Makepad settings.",
            string.Empty,
            "## What The Companion Manages",
            string.Empty,
            "- Android platform-tools / adb",
            "- Meta VR CLI / hzdb compatibility tooling",
            "- scrcpy for display casting",
            "- optional FFmpeg media runtime for decode/probe previews",
            "- catalog APK downloads and verification bundles",
            string.Empty,
            "## Source APK Build Prerequisites",
            string.Empty,
            "- .NET 10 SDK for companion source builds",
            "- Rust with the aarch64-linux-android target for Rusty XR APK examples",
            "- Android SDK, NDK, build tools, and JDK layout accepted by the Rusty XR example build scripts",
            "- Quest-compatible OpenXR loader only for the immersive composite-layer example",
            "- Optional compliant Android liblsl.so only for LSL-capable broker APK builds",
            string.Empty,
            "## Agent Steps",
            string.Empty
        });

        lines.AddRange(status.AgentSteps.Select(static step => $"- {step}"));
        lines.AddRange(new[] { string.Empty, "## Commands", string.Empty });
        foreach (var command in status.Commands)
        {
            lines.Add($"### {command.Id}");
            lines.Add(command.Description);
            lines.Add(string.Empty);
            lines.Add("```powershell");
            lines.Add($"cd \"{command.WorkingDirectory}\"");
            lines.Add(command.Command);
            lines.Add("```");
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ResolveWorkspaceRoot(string? workspaceRoot, string currentDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return Path.GetFullPath(workspaceRoot);
        }

        var current = Path.GetFullPath(currentDirectory);
        var companionAncestor = FindAncestorNamed(current, CompanionRepoName);
        if (companionAncestor is not null)
        {
            return Directory.GetParent(companionAncestor)?.FullName ?? current;
        }

        var rustyXrAncestor = FindAncestorNamed(current, RustyXrRepoName);
        if (rustyXrAncestor is not null)
        {
            return Directory.GetParent(rustyXrAncestor)?.FullName ?? current;
        }

        return current;
    }

    private static string? FindAncestorNamed(string start, string name)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (string.Equals(directory.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static SourceWorkspaceRepository RepositoryStatus(string name, string path, string role)
    {
        return new SourceWorkspaceRepository(
            name,
            path,
            Directory.Exists(Path.Combine(path, ".git")) ||
                File.Exists(Path.Combine(path, "Cargo.toml")) ||
                File.Exists(Path.Combine(path, "Justfile")) ||
                File.Exists(Path.Combine(path, "apps", "hostess-t-makepad", "Cargo.toml")),
            role);
    }

    private static string FoundLabel(bool found) => found ? "found" : "missing";
}

public sealed record SourceWorkspaceStatus(
    string WorkspaceRoot,
    string RustyXrRepoPath,
    string CompanionRepoPath,
    bool RustyXrRepoPresent,
    bool CompanionRepoPresent,
    string CompanionCliProjectPath,
    string MinimalBuildScriptPath,
    string MinimalCatalogPath,
    string MinimalApkPath,
    bool MinimalApkPresent,
    string CompositeBuildScriptPath,
    string CompositeCatalogPath,
    string CompositeApkPath,
    bool CompositeApkPresent,
    string BrokerBuildScriptPath,
    string BrokerCatalogPath,
    string BrokerApkPath,
    bool BrokerApkPresent,
    IReadOnlyList<string> AgentSteps,
    IReadOnlyList<SourceWorkspaceRepository> OptionalMorphospaceRepos,
    IReadOnlyList<SourceWorkspaceCommand> Commands);

public sealed record SourceWorkspaceRepository(
    string Name,
    string Path,
    bool Present,
    string Role);

public sealed record SourceWorkspaceCommand(
    string Id,
    string Description,
    string WorkingDirectory,
    string Command);
