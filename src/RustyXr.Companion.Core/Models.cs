namespace RustyXr.Companion.Core;

public enum ToolKind
{
    Adb,
    Hzdb,
    Scrcpy
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
               (string.Equals(depth, "visualize", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(depth, "diagnostic", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(depth, "diagnostics", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(depth, "status", StringComparison.OrdinalIgnoreCase));
    }

    public static bool RequiresLogValidation(RuntimeProfile? profile) =>
        RequiresAlignedGpuProjection(profile) || RequiresEnvironmentDepthDiagnostics(profile);

    public static RuntimeProfileLogValidation Validate(RuntimeProfile? profile, string? logcatText)
    {
        if (RequiresEnvironmentDepthDiagnostics(profile))
        {
            return ValidateEnvironmentDepthDiagnostics(logcatText);
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

    private static RuntimeProfileLogValidation ValidateEnvironmentDepthDiagnostics(string? logcatText)
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
            hasVisualizerDraw)
        {
            return new RuntimeProfileLogValidation(
                true,
                $"Runtime profile reported active environment-depth acquisition with {acquired} acquired frame(s), {uniqueCaptureTimes} unique capture timestamp(s), OpenXR frame {openXrFrameCount}, depth range metadata, explicit confidence state, and D16 grayscale visualizer draws.");
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

        return new RuntimeProfileLogValidation(
            false,
            $"Runtime profile requires environment-depth acquisition diagnostics; missing {string.Join(", ", missing)}.");
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
