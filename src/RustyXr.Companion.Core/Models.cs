namespace RustyXr.Companion.Core;

public enum ToolKind
{
    Adb,
    Hzdb,
    Scrcpy,
    Ffmpeg,
    Ffprobe
}

public sealed record ToolStatus(
    ToolKind Kind,
    string DisplayName,
    bool IsAvailable,
    string? Path,
    string? Version,
    string Detail);

public sealed record QuestEndpoint(string Host, int Port)
{
    public const int DefaultAdbPort = 5555;

    public static bool TryParse(string raw, out QuestEndpoint endpoint)
    {
        endpoint = new QuestEndpoint(string.Empty, DefaultAdbPort);
        var value = raw.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separatorIndex = value.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            endpoint = new QuestEndpoint(value, DefaultAdbPort);
            return true;
        }

        var host = value[..separatorIndex].Trim();
        var portText = value[(separatorIndex + 1)..].Trim();
        if (host.Length == 0 || !int.TryParse(portText, out var port) || port is <= 0 or > 65535)
        {
            return false;
        }

        endpoint = new QuestEndpoint(host, port);
        return true;
    }

    public override string ToString() => $"{Host}:{Port}";
}

public sealed record QuestDevice(
    string Serial,
    string State,
    string? Model = null,
    string? Product = null)
{
    public bool IsOnline => string.Equals(State, "device", StringComparison.OrdinalIgnoreCase);
    public string Label => string.IsNullOrWhiteSpace(Model) ? Serial : $"{Model} ({Serial})";
}

public sealed record QuestSnapshot(
    string Serial,
    string Model,
    string Battery,
    string Wakefulness,
    string Foreground,
    DateTimeOffset CapturedAt,
    int? HeadsetBatteryLevel = null,
    string HeadsetBatteryStatus = "",
    bool? IsAwake = null,
    bool? IsInteractive = null,
    string DisplayPowerState = "",
    IReadOnlyList<QuestControllerStatus>? Controllers = null,
    QuestProximityStatus? Proximity = null,
    QuestForegroundStatus? ForegroundStatus = null);

public sealed record QuestForegroundStatus(
    string ResumedActivity,
    string FocusedWindow,
    string FocusedApp,
    bool HasKnownBlocker,
    bool FocusDiffersFromResumed,
    string BlockerLabel,
    string Detail);

public sealed record QuestControllerStatus(
    string HandLabel,
    int? BatteryLevel,
    string ConnectionState,
    string DeviceId)
{
    public string BatteryLabel => BatteryLevel is int level ? $"{level}%" : "n/a";
    public string Detail => $"{HandLabel} controller: {BatteryLabel}; {ConnectionStateLabel}";
    public string ConnectionStateLabel => string.IsNullOrWhiteSpace(ConnectionState) ? "connection unknown" : ConnectionState;
}

public sealed record QuestPowerStatus(
    string Wakefulness,
    bool? IsInteractive,
    string DisplayPowerState,
    bool? IsAwake,
    string Detail);

public sealed record QuestWifiAdbResult(
    string Serial,
    QuestEndpoint Endpoint,
    CommandResult TcpipResult,
    CommandResult ConnectResult,
    DateTimeOffset CompletedAt)
{
    public bool Succeeded => TcpipResult.Succeeded && ConnectResult.Succeeded;
    public string Detail => string.Join(
        Environment.NewLine,
        new[] { TcpipResult.CondensedOutput, ConnectResult.CondensedOutput }
            .Where(static value => !string.IsNullOrWhiteSpace(value)));
}

public sealed record QuestProximityStatus(
    bool Available,
    bool HoldActive,
    string VirtualState,
    bool IsAutosleepDisabled,
    string HeadsetState,
    int? AutoSleepTimeMs,
    DateTimeOffset RetrievedAt,
    DateTimeOffset? HoldUntil,
    string Detail);

public sealed record QuestScreenshotCapture(
    bool Succeeded,
    string OutputPath,
    string Method,
    string Detail,
    DateTimeOffset CapturedAt);

public sealed record QuestAppDiagnostics(
    string PackageName,
    bool ProcessRunning,
    string? ProcessId,
    bool ForegroundMatchesPackage,
    string Foreground,
    string GfxInfoSummary,
    string MemorySummary,
    DateTimeOffset CapturedAt);

public sealed record MediaFrameRecord(
    long FrameIndex,
    string Stream,
    string Format,
    int ByteLength,
    int? Width,
    int? Height,
    long? TimestampNs,
    string PayloadPath,
    DateTimeOffset ReceivedAt);

public sealed record MediaReceiverResult(
    string Host,
    int Port,
    string OutputDirectory,
    int FrameCount,
    IReadOnlyList<MediaFrameRecord> Frames,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public enum OscArgumentKind
{
    Int,
    Float,
    String,
    Blob,
    Bool,
    Nil,
    Impulse
}

public sealed record OscArgument(
    OscArgumentKind Kind,
    int? IntValue = null,
    float? FloatValue = null,
    string? StringValue = null,
    bool? BoolValue = null,
    byte[]? BlobValue = null)
{
    public char TypeTag => Kind switch
    {
        OscArgumentKind.Int => 'i',
        OscArgumentKind.Float => 'f',
        OscArgumentKind.String => 's',
        OscArgumentKind.Blob => 'b',
        OscArgumentKind.Bool => BoolValue == true ? 'T' : 'F',
        OscArgumentKind.Nil => 'N',
        OscArgumentKind.Impulse => 'I',
        _ => throw new InvalidOperationException($"Unsupported OSC argument kind: {Kind}.")
    };

    public static OscArgument Int(int value) => new(OscArgumentKind.Int, IntValue: value);

    public static OscArgument Float(float value) => new(OscArgumentKind.Float, FloatValue: value);

    public static OscArgument String(string value) => new(OscArgumentKind.String, StringValue: value);

    public static OscArgument Blob(byte[] value) => new(OscArgumentKind.Blob, BlobValue: value);

    public static OscArgument Bool(bool value) => new(OscArgumentKind.Bool, BoolValue: value);

    public static OscArgument Nil() => new(OscArgumentKind.Nil);

    public static OscArgument Impulse() => new(OscArgumentKind.Impulse);
}

public sealed record OscMessage(
    string Address,
    IReadOnlyList<OscArgument> Arguments)
{
    public bool IsValid => ValidAddress(Address);

    public static bool ValidAddress(string address) =>
        !string.IsNullOrWhiteSpace(address) &&
        address.StartsWith("/", StringComparison.Ordinal) &&
        !address.Contains("\0", StringComparison.Ordinal);
}

public sealed record OscSendResult(
    string Host,
    int Port,
    OscMessage Message,
    int ByteCount,
    DateTimeOffset SentAt);

public sealed record OscReceivedMessage(
    OscMessage Message,
    string PeerAddress,
    int ByteCount,
    DateTimeOffset ReceivedAt);

public sealed record OscReceiveResult(
    string Host,
    int Port,
    int PacketCount,
    IReadOnlyList<OscReceivedMessage> Packets,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed record QuestAppTarget(
    string Id,
    string Label,
    string PackageName,
    string? ActivityName,
    string? ApkFile,
    string Description);

public sealed record DeviceProperty(string Key, string Value);

public sealed record DeviceProfile(
    string Id,
    string Label,
    IReadOnlyList<DeviceProperty> Properties,
    string Description);

public sealed record RuntimeProfile(
    string Id,
    string Label,
    IReadOnlyDictionary<string, string> Values,
    string Description);

public static class RuntimeProfileSafety
{
    public const string StrobeWarning =
        "WARNING: This runtime profile intentionally uses strobing light. It can trigger seizures or other adverse reactions in people with photosensitive epilepsy or light-sensitive conditions. Use only with explicit informed opt-in.";

    public static bool UsesIntentionalStrobe(RuntimeProfile? profile)
    {
        if (profile is null)
        {
            return false;
        }

        return NumericValue(profile, "rustyxr.fullFieldFlickerHz") > 0.0 ||
               NumericValue(profile, "rustyxr.passthroughLutFlickerHz") > 0.0 ||
               profile.Id.Contains("flicker", StringComparison.OrdinalIgnoreCase) ||
               profile.Description.Contains("strob", StringComparison.OrdinalIgnoreCase);
    }

    private static double NumericValue(RuntimeProfile profile, string key)
    {
        if (!profile.Values.TryGetValue(key, out var raw))
        {
            return 0.0;
        }

        return double.TryParse(
            raw,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0.0;
    }
}

public sealed record RuntimeProfileLogValidation(bool Succeeded, string Detail);

public static class RuntimeProfileLogValidator
{
    public static bool RequiresAlignedGpuProjection(RuntimeProfile? profile)
    {
        if (profile is null)
        {
            return false;
        }

        if (string.Equals(profile.Id, "camera-stereo-gpu-composite", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return profile.Values.TryGetValue("rustyxr.cameraTier", out var tier) &&
               (string.Equals(tier, "gpu-projected", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tier, "camera-stereo-gpu-composite", StringComparison.OrdinalIgnoreCase));
    }

    public static bool RequiresEnvironmentDepthDiagnostics(RuntimeProfile? profile)
    {
        if (profile is null)
        {
            return false;
        }

        if (string.Equals(profile.Id, "environment-depth-diagnostics", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return profile.Values.TryGetValue("rustyxr.depth", out var depth) &&
               IsEnvironmentDepthValidationMode(depth);
    }

    public static bool RequiresHandMeshParticles(RuntimeProfile? profile)
    {
        if (profile is null)
        {
            return false;
        }

        if (string.Equals(profile.Id, "meta-hand-mesh-particles", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return profile.Values.TryGetValue("rustyxr.handParticles", out var mode) &&
               IsOpenXrHandMeshMode(mode);
    }

    public static bool RequiresLogValidation(RuntimeProfile? profile) =>
        RequiresAlignedGpuProjection(profile) ||
        RequiresEnvironmentDepthDiagnostics(profile) ||
        RequiresHandMeshParticles(profile);

    public static RuntimeProfileLogValidation Validate(RuntimeProfile? profile, string? logcatText)
    {
        if (RequiresEnvironmentDepthDiagnostics(profile))
        {
            return ValidateEnvironmentDepthDiagnostics(profile, logcatText);
        }

        if (RequiresHandMeshParticles(profile))
        {
            return ValidateHandMeshParticles(profile, logcatText);
        }

        if (!RequiresAlignedGpuProjection(profile))
        {
            return new RuntimeProfileLogValidation(true, "No aligned GPU projection log validation required.");
        }

        if (string.IsNullOrWhiteSpace(logcatText))
        {
            return new RuntimeProfileLogValidation(
                false,
                "Runtime profile requires activeTier=gpu-projected and alignedProjection=true; capture logcat with --logcat-lines.");
        }

        var statusLine = FindLatestProjectionStatusLine(logcatText);
        var hasProjectedTier = statusLine?.Contains("activeTier=gpu-projected", StringComparison.Ordinal) == true;
        var hasAlignedProjection = statusLine?.Contains("alignedProjection=true", StringComparison.Ordinal) == true;
        var hasSeparateStereo = statusLine?.Contains("stereoLayout=Separate", StringComparison.Ordinal) == true;
        var hasPairedGpuBuffers = statusLine?.Contains("pairedLeftRightGpuBuffers=true", StringComparison.Ordinal) == true;
        var hasAcceptedPoseSource =
            statusLine?.Contains("poseSource=platform", StringComparison.Ordinal) == true ||
            statusLine?.Contains("poseSource=estimated-profile", StringComparison.Ordinal) == true;
        var hasTextureOrientation =
            statusLine?.Contains("leftCameraTextureTransform=", StringComparison.Ordinal) == true &&
            statusLine?.Contains("rightCameraTextureTransform=", StringComparison.Ordinal) == true &&
            statusLine?.Contains("sourceEyeMapping=", StringComparison.Ordinal) == true &&
            statusLine?.Contains("orientationCheck=true", StringComparison.Ordinal) == true;
        var hasVisualAcceptance =
            statusLine?.Contains("visualInspection=accepted", StringComparison.Ordinal) == true &&
            statusLine?.Contains("visualReleaseAccepted=true", StringComparison.Ordinal) == true &&
            statusLine?.Contains("orientationAccepted=true", StringComparison.Ordinal) == true;
        var requiresVisualInspection =
            statusLine?.Contains("visualInspection=required", StringComparison.Ordinal) == true ||
            statusLine?.Contains("visualReleaseAccepted=false", StringComparison.Ordinal) == true;
        var hasFocused = statusLine?.Contains("openXrFocused=true", StringComparison.Ordinal) == true ||
                         statusLine?.Contains("OpenXR FOCUSED", StringComparison.Ordinal) == true;
        var cpuUploadCountZero =
            statusLine?.Contains("cpuUploadCount=0", StringComparison.Ordinal) == true &&
            !logcatText.Contains("uploaded diagnostic flat camera copy", StringComparison.Ordinal);
        var lastFrame = 0;
        var hasFrameCadence =
            statusLine is not null &&
            TryReadTokenInt(statusLine, "openXrFrameCount=", out lastFrame) &&
            lastFrame >= 360;
        var hasNoLifetimeWarnings =
            !logcatText.Contains("HardwareBuffer lifetime warning", StringComparison.OrdinalIgnoreCase) &&
            !logcatText.Contains("AHardwareBuffer lifetime warning", StringComparison.OrdinalIgnoreCase);

        if (hasProjectedTier &&
            hasAlignedProjection &&
            hasSeparateStereo &&
            hasPairedGpuBuffers &&
            hasAcceptedPoseSource &&
            hasTextureOrientation &&
            hasVisualAcceptance &&
            hasFocused &&
            cpuUploadCountZero &&
            hasFrameCadence &&
            hasNoLifetimeWarnings)
        {
            return new RuntimeProfileLogValidation(
                true,
                $"Runtime profile reported activeTier=gpu-projected, alignedProjection=true, paired stereo GPU buffers, accepted pose source, explicit per-eye texture orientation, manual visual acceptance, and OpenXR frame {lastFrame}.");
        }

        var observedText = statusLine ?? logcatText;
        var observedTier = LastTokenAfter(observedText, "activeTier=") ?? "missing";
        var observedAlignment = LastTokenAfter(observedText, "alignedProjection=") ?? "missing";
        var missing = new List<string>();
        if (!hasProjectedTier)
        {
            missing.Add("activeTier=gpu-projected");
        }
        if (!hasAlignedProjection)
        {
            missing.Add("alignedProjection=true");
        }
        if (!hasSeparateStereo)
        {
            missing.Add("stereoLayout=Separate");
        }
        if (!hasPairedGpuBuffers)
        {
            missing.Add("pairedLeftRightGpuBuffers=true");
        }
        if (!hasAcceptedPoseSource)
        {
            missing.Add("poseSource=platform or poseSource=estimated-profile");
        }
        if (!hasTextureOrientation)
        {
            missing.Add("sourceEyeMapping, left/right camera texture transforms, and orientationCheck=true");
        }
        if (!hasVisualAcceptance)
        {
            missing.Add(requiresVisualInspection
                ? "manual visual release acceptance"
                : "visualInspection=accepted and visualReleaseAccepted=true");
        }
        if (!hasFocused)
        {
            missing.Add("OpenXR FOCUSED");
        }
        if (!cpuUploadCountZero)
        {
            missing.Add("zero CPU diagnostic upload");
        }
        if (!hasFrameCadence)
        {
            missing.Add("OpenXR frame cadence evidence >= 360 frames");
        }
        if (!hasNoLifetimeWarnings)
        {
            missing.Add("no HardwareBuffer lifetime warnings");
        }
        return new RuntimeProfileLogValidation(
            false,
            $"Runtime profile requires real stereo GPU projection; observed activeTier={observedTier} alignedProjection={observedAlignment}; missing {string.Join(", ", missing)}.");
    }

    private static RuntimeProfileLogValidation ValidateEnvironmentDepthDiagnostics(
        RuntimeProfile? profile,
        string? logcatText)
    {
        if (string.IsNullOrWhiteSpace(logcatText))
        {
            return new RuntimeProfileLogValidation(
                false,
                "Runtime profile requires an environment-depth status line; capture logcat with --logcat-lines.");
        }

        var statusLine = FindLatestEnvironmentDepthStatusLine(logcatText);
        if (statusLine is null)
        {
            return new RuntimeProfileLogValidation(
                false,
                "Runtime profile requires a `Rusty XR environment depth status` log line.");
        }

        var mode = profile is not null && profile.Values.TryGetValue("rustyxr.depth", out var rawMode)
            ? rawMode
            : string.Empty;
        var requiresMeshOverlay = IsEnvironmentDepthMeshOverlayMode(mode);
        var requiresParticleOverlay = IsEnvironmentDepthParticleOverlayMode(mode);
        var hasEnabled = statusLine.Contains("depthEnabled=true", StringComparison.Ordinal);
        var hasExtension = statusLine.Contains("extensionAvailable=true", StringComparison.Ordinal);
        var hasSupported = statusLine.Contains("supported=true", StringComparison.Ordinal);
        var hasProviderCreated = statusLine.Contains("providerCreated=true", StringComparison.Ordinal);
        var hasProviderRunning = statusLine.Contains("providerRunning=true", StringComparison.Ordinal);
        var hasSwapchain = statusLine.Contains("swapchainCreated=true", StringComparison.Ordinal);
        var hasVisualizer = statusLine.Contains("visualizer=true", StringComparison.Ordinal);
        var attempts = 0;
        var acquired = 0;
        var uniqueCaptureTimes = 0;
        var openXrFrameCount = 0;
        var hasAttempts = TryReadTokenInt(statusLine, "acquireAttempts=", out attempts) && attempts > 0;
        var hasAcquired = TryReadTokenInt(statusLine, "acquiredFrames=", out acquired) && acquired > 0;
        var hasUniqueCaptureTime =
            TryReadTokenInt(statusLine, "uniqueCaptureTimes=", out uniqueCaptureTimes) &&
            uniqueCaptureTimes > 0;
        var hasFrameCadence =
            TryReadTokenInt(statusLine, "openXrFrameCount=", out openXrFrameCount) &&
            openXrFrameCount >= 120;
        var hasCaptureTimestamp = statusLine.Contains("captureTimeNs=", StringComparison.Ordinal) &&
                                  !statusLine.Contains("captureTimeNs=none", StringComparison.Ordinal);
        var hasDepthRange = statusLine.Contains("nearZ=", StringComparison.Ordinal) &&
                            statusLine.Contains("farZ=", StringComparison.Ordinal);
        var hasConfidenceState =
            statusLine.Contains("confidenceSource=", StringComparison.Ordinal) &&
            statusLine.Contains("confidencePayload=", StringComparison.Ordinal);
        var hasConfidenceStatus = statusLine.Contains("confidenceStatus=", StringComparison.Ordinal);
        var hasDepthTextureFormat = statusLine.Contains("depthFormat=VK_FORMAT_D16_UNORM", StringComparison.Ordinal);
        var hasEyeMapping = statusLine.Contains("depthVisualEyeMapping=left-layer-0-right-layer-1", StringComparison.Ordinal);
        var hasDepthTextureTransform = statusLine.Contains("depthVisualTextureTransform=rotate0+flipY", StringComparison.Ordinal);
        var visualizerDrawLine = FindLatestEnvironmentDepthVisualizerDrawLine(logcatText);
        var hasVisualizerDraw =
            visualizerDrawLine is not null &&
            visualizerDrawLine.Contains("depthTextureFormat=VK_FORMAT_D16_UNORM", StringComparison.Ordinal) &&
            visualizerDrawLine.Contains("grayscale=linear-d16-meters-infinity-white", StringComparison.Ordinal) &&
            visualizerDrawLine.Contains("depthVisualTextureTransform=rotate0+flipY", StringComparison.Ordinal);
        var meshOverlayDrawLine = FindLatestEnvironmentDepthMeshOverlayDrawLine(logcatText);
        var hasMeshOverlayDraw =
            !requiresMeshOverlay ||
            (meshOverlayDrawLine is not null &&
             meshOverlayDrawLine.Contains("projection=local-space-depth-surface", StringComparison.Ordinal) &&
             meshOverlayDrawLine.Contains("rasterization=world-space-generated-grid", StringComparison.Ordinal) &&
             meshOverlayDrawLine.Contains("dominantSurfaceGrid=true", StringComparison.Ordinal) &&
             meshOverlayDrawLine.Contains("screenUvGrid=false", StringComparison.Ordinal) &&
             meshOverlayDrawLine.Contains("passthroughVisible=true", StringComparison.Ordinal));
        var particleOverlayDrawLine = FindLatestEnvironmentDepthParticleOverlayDrawLine(logcatText);
        var hasParticleOverlayDraw =
            !requiresParticleOverlay ||
            (particleOverlayDrawLine is not null &&
             particleOverlayDrawLine.Contains("projection=local-space-retained-particles", StringComparison.Ordinal) &&
             particleOverlayDrawLine.Contains("rasterization=metric-billboard-particles", StringComparison.Ordinal) &&
             particleOverlayDrawLine.Contains("particleCapacity=", StringComparison.Ordinal) &&
             particleOverlayDrawLine.Contains("particleVertexCount=", StringComparison.Ordinal) &&
             particleOverlayDrawLine.Contains("passthroughVisible=true", StringComparison.Ordinal));

        if (hasEnabled &&
            hasExtension &&
            hasSupported &&
            hasProviderCreated &&
            hasProviderRunning &&
            hasSwapchain &&
            hasVisualizer &&
            hasAttempts &&
            hasAcquired &&
            hasUniqueCaptureTime &&
            hasFrameCadence &&
            hasCaptureTimestamp &&
            hasDepthRange &&
            hasConfidenceState &&
            hasConfidenceStatus &&
            hasDepthTextureFormat &&
            hasEyeMapping &&
            hasDepthTextureTransform &&
            hasVisualizerDraw &&
            hasMeshOverlayDraw &&
            hasParticleOverlayDraw)
        {
            var overlayDetail = requiresMeshOverlay
                ? ", and local-space depth mesh overlay draw"
                : requiresParticleOverlay
                    ? ", and retained local-space particle overlay draw"
                    : string.Empty;
            return new RuntimeProfileLogValidation(
                true,
                $"Runtime profile reported active environment-depth acquisition with {acquired} acquired frame(s), {uniqueCaptureTimes} unique capture timestamp(s), OpenXR frame {openXrFrameCount}, depth range metadata, explicit confidence state, D16 grayscale visualizer draws{overlayDetail}.");
        }

        var missing = new List<string>();
        if (!hasEnabled)
        {
            missing.Add("depthEnabled=true");
        }
        if (!hasExtension)
        {
            missing.Add("extensionAvailable=true");
        }
        if (!hasSupported)
        {
            missing.Add("supported=true");
        }
        if (!hasProviderCreated)
        {
            missing.Add("providerCreated=true");
        }
        if (!hasProviderRunning)
        {
            missing.Add("providerRunning=true");
        }
        if (!hasSwapchain)
        {
            missing.Add("swapchainCreated=true");
        }
        if (!hasVisualizer)
        {
            missing.Add("visualizer=true");
        }
        if (!hasAttempts)
        {
            missing.Add("acquireAttempts>0");
        }
        if (!hasAcquired)
        {
            missing.Add("acquiredFrames>0");
        }
        if (!hasUniqueCaptureTime)
        {
            missing.Add("uniqueCaptureTimes>0");
        }
        if (!hasFrameCadence)
        {
            missing.Add("OpenXR frame cadence evidence >= 120 frames");
        }
        if (!hasCaptureTimestamp)
        {
            missing.Add("runtime captureTimeNs");
        }
        if (!hasDepthRange)
        {
            missing.Add("nearZ/farZ");
        }
        if (!hasConfidenceState)
        {
            missing.Add("confidenceSource/confidencePayload state");
        }
        if (!hasConfidenceStatus)
        {
            missing.Add("confidenceStatus");
        }
        if (!hasDepthTextureFormat)
        {
            missing.Add("depthFormat=VK_FORMAT_D16_UNORM");
        }
        if (!hasEyeMapping)
        {
            missing.Add("left/right depth eye mapping");
        }
        if (!hasDepthTextureTransform)
        {
            missing.Add("depth texture orientation transform");
        }
        if (!hasVisualizerDraw)
        {
            missing.Add("environment depth visualizer draw");
        }
        if (!hasMeshOverlayDraw)
        {
            missing.Add("local-space depth mesh overlay draw");
        }
        if (!hasParticleOverlayDraw)
        {
            missing.Add("retained local-space depth particle overlay draw");
        }

        return new RuntimeProfileLogValidation(
            false,
            $"Runtime profile requires environment-depth acquisition diagnostics; missing {string.Join(", ", missing)}.");
    }

    private static RuntimeProfileLogValidation ValidateHandMeshParticles(
        RuntimeProfile? profile,
        string? logcatText)
    {
        if (string.IsNullOrWhiteSpace(logcatText))
        {
            return new RuntimeProfileLogValidation(
                false,
                "Runtime profile requires a hand-mesh particle draw line; capture logcat with --logcat-lines.");
        }

        var drawLine = FindLatestHandMeshParticleDrawLine(logcatText);
        if (drawLine is null)
        {
            return new RuntimeProfileLogValidation(
                false,
                "Runtime profile requires a `Rusty XR hand mesh particle draw` log line.");
        }

        var particles = 0;
        var vertexCount = 0;
        var hasParticles = TryReadTokenInt(drawLine, "particles=", out particles) && particles > 0;
        var hasVertexCount = TryReadTokenInt(drawLine, "vertexCount=", out vertexCount) && vertexCount > 0;
        var hasSampler = drawLine.Contains("sampler=LiveHandMeshParticleSampler", StringComparison.Ordinal);
        var hasPassthrough = drawLine.Contains("passthroughVisible=true", StringComparison.Ordinal);
        var mode = LastTokenAfter(drawLine, "mode=") ?? "missing";
        var requiresOpenXrHandMesh = RequiresOpenXrHandMesh(profile);
        var openXrLine = FindLatestOpenXrHandMeshParticleLine(logcatText);
        var activeHands = 0;
        var hasOpenXrHandMesh =
            !requiresOpenXrHandMesh ||
            (openXrLine is not null &&
             TryReadTokenInt(openXrLine, "activeHands=", out activeHands) &&
             activeHands > 0 &&
             openXrLine.Contains("skinning=cpu-linear-blend", StringComparison.Ordinal) &&
             openXrLine.Contains("bindMesh=XR_FB_hand_tracking_mesh", StringComparison.Ordinal));

        if (hasParticles &&
            hasVertexCount &&
            hasSampler &&
            hasPassthrough &&
            hasOpenXrHandMesh)
        {
            var sourceDetail = requiresOpenXrHandMesh
                ? $" from {activeHands} OpenXR hand mesh hand(s)"
                : string.Empty;
            return new RuntimeProfileLogValidation(
                true,
                $"Runtime profile reported {particles} hand-mesh particle(s), {vertexCount} billboard vertices, stable Rusty XR sampler output, and passthrough-visible rendering{sourceDetail}.");
        }

        var missing = new List<string>();
        if (!hasParticles)
        {
            missing.Add("particles>0");
        }
        if (!hasVertexCount)
        {
            missing.Add("vertexCount>0");
        }
        if (!hasSampler)
        {
            missing.Add("sampler=LiveHandMeshParticleSampler");
        }
        if (!hasPassthrough)
        {
            missing.Add("passthroughVisible=true");
        }
        if (!hasOpenXrHandMesh)
        {
            missing.Add("OpenXR XR_FB_hand_tracking_mesh activeHands>0");
        }

        return new RuntimeProfileLogValidation(
            false,
            $"Runtime profile requires active hand-mesh particle rendering; observed mode={mode}; missing {string.Join(", ", missing)}.");
    }

    private static string? FindLatestProjectionStatusLine(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var finalStatusLine = lines
            .LastOrDefault(line => line.Contains("Rusty XR final projection status", StringComparison.Ordinal));
        if (finalStatusLine is not null)
        {
            return finalStatusLine;
        }

        return lines.LastOrDefault(line =>
            line.Contains("Rusty XR GPU stereo camera draw prepared", StringComparison.Ordinal));
    }

    private static string? FindLatestEnvironmentDepthStatusLine(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return lines.LastOrDefault(line =>
            line.Contains("Rusty XR environment depth status", StringComparison.Ordinal));
    }

    private static string? FindLatestEnvironmentDepthVisualizerDrawLine(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return lines.LastOrDefault(line =>
            line.Contains("Rusty XR environment depth visualizer draw", StringComparison.Ordinal));
    }

    private static string? FindLatestEnvironmentDepthMeshOverlayDrawLine(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return lines.LastOrDefault(line =>
            line.Contains("Rusty XR environment depth mesh overlay draw", StringComparison.Ordinal));
    }

    private static string? FindLatestEnvironmentDepthParticleOverlayDrawLine(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return lines.LastOrDefault(line =>
            line.Contains("Rusty XR environment depth particle overlay draw", StringComparison.Ordinal));
    }

    private static string? FindLatestHandMeshParticleDrawLine(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return lines.LastOrDefault(line =>
            line.Contains("Rusty XR hand mesh particle draw", StringComparison.Ordinal));
    }

    private static string? FindLatestOpenXrHandMeshParticleLine(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return lines.LastOrDefault(line =>
            line.Contains("Rusty XR OpenXR hand mesh particles", StringComparison.Ordinal));
    }

    private static bool IsEnvironmentDepthValidationMode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "status" or "diagnostic" or "diagnostics" or "on" or "true" or "1" or
            "visualize" or "visualise" or "visual" or "debug-visual" or
            "mesh" or "mesh-overlay" or "depth-mesh" or "debug-mesh" or "wire-mesh" or
            "particles" or "particle-overlay" or "depth-particles" or "surface-particles" => true,
            _ => false
        };
    }

    private static bool IsEnvironmentDepthMeshOverlayMode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "mesh" or "mesh-overlay" or "depth-mesh" or "debug-mesh" or "wire-mesh" => true,
            _ => false
        };
    }

    private static bool IsEnvironmentDepthParticleOverlayMode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "particles" or "particle-overlay" or "depth-particles" or "surface-particles" => true,
            _ => false
        };
    }

    private static bool RequiresOpenXrHandMesh(RuntimeProfile? profile)
    {
        if (profile is null)
        {
            return false;
        }

        if (string.Equals(profile.Id, "meta-hand-mesh-particles", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return profile.Values.TryGetValue("rustyxr.handParticles", out var mode) &&
               IsOpenXrHandMeshMode(mode);
    }

    private static bool IsOpenXrHandMeshMode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "hand-mesh" or "on" or "true" or "1" or "meta" or "meta-hand" or
            "meta-hand-mesh" or "openxr" or "openxr-hand" or "openxr-hand-mesh" or
            "runtime" or "runtime-hand" or "runtime-hand-mesh" or "real" or "real-hand" or
            "real-hand-mesh" => true,
            _ => false
        };
    }

    private static string? LastTokenAfter(string text, string prefix)
    {
        var index = text.LastIndexOf(prefix, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var start = index + prefix.Length;
        var end = start;
        while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not ',' and not ';')
        {
            end++;
        }

        return end > start ? text[start..end] : null;
    }

    private static bool TryReadLastOpenXrFrame(string text, out int frame)
    {
        frame = 0;
        var marker = "Rusty XR OpenXR frame ";
        var index = text.LastIndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return false;
        }

        var start = index + marker.Length;
        var end = start;
        while (end < text.Length && char.IsDigit(text[end]))
        {
            end++;
        }

        return end > start && int.TryParse(text[start..end], out frame);
    }

    private static bool TryReadTokenInt(string text, string prefix, out int value)
    {
        value = 0;
        var index = text.LastIndexOf(prefix, StringComparison.Ordinal);
        if (index < 0)
        {
            return false;
        }

        var start = index + prefix.Length;
        var end = start;
        while (end < text.Length && char.IsDigit(text[end]))
        {
            end++;
        }

        return end > start && int.TryParse(text[start..end], out value);
    }
}

public static class CameraSourceDiagnosticsLogExtractor
{
    public const string Marker = "Rusty XR camera source diagnostics JSON:";

    public static bool TryExtract(string? logcatText, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrWhiteSpace(logcatText))
        {
            return false;
        }

        var markerIndex = logcatText.LastIndexOf(Marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var start = logcatText.IndexOf('{', markerIndex);
        if (start < 0)
        {
            return false;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = start; index < logcatText.Length; index++)
        {
            var c = logcatText[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    json = logcatText[start..(index + 1)];
                    return true;
                }
            }
        }

        return false;
    }
}

public sealed record CatalogAppSelection(
    QuestSessionCatalog Catalog,
    QuestAppTarget App,
    string CatalogPath,
    string? ResolvedApkPath);

public sealed record QuestSessionCatalog(
    string SchemaVersion,
    IReadOnlyList<QuestAppTarget> Apps,
    IReadOnlyList<DeviceProfile> DeviceProfiles,
    IReadOnlyList<RuntimeProfile> RuntimeProfiles)
{
    public const string CurrentSchemaVersion = "rusty.xr.quest-app-catalog.v1";
}

public sealed record CommandResult(
    string FileName,
    string Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration)
{
    public bool Succeeded => ExitCode == 0;
    public string CondensedOutput
    {
        get
        {
            var text = string.Join(
                Environment.NewLine,
                new[] { StandardOutput.Trim(), StandardError.Trim() }.Where(static value => value.Length > 0));
            return text.Length <= 800 ? text : text[..797] + "...";
        }
    }
}

public sealed record StreamLaunchRequest(
    string Serial,
    int? MaxSize = null,
    int? BitRateMbps = null,
    bool StayAwake = true);

public sealed record StreamSession(
    string ToolPath,
    string Arguments,
    DateTimeOffset StartedAt,
    int ProcessId);
