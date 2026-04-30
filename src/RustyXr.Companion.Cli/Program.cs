using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RustyXr.Companion.Core;
using RustyXr.Companion.Diagnostics;

var exitCode = await CliProgram.RunAsync(args).ConfigureAwait(false);
return exitCode;

internal static class CliProgram
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            WriteHelp();
            return 0;
        }

        if (args[0] is "-v" or "--version")
        {
            return Version(ArgOptions.Parse(args.Skip(1)));
        }

        try
        {
            var command = args[0].ToLowerInvariant();
            var options = ArgOptions.Parse(args.Skip(1));
            return command switch
            {
                "version" => Version(options),
                "doctor" => await DoctorAsync(options).ConfigureAwait(false),
                "devices" => await DevicesAsync(options).ConfigureAwait(false),
                "connect" => await ConnectAsync(options).ConfigureAwait(false),
                "snapshot" => await SnapshotAsync(options).ConfigureAwait(false),
                "install" => await InstallAsync(options).ConfigureAwait(false),
                "launch" => await LaunchAsync(options).ConfigureAwait(false),
                "stop" => await StopAsync(options).ConfigureAwait(false),
                "profile" => await ProfileAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "cast" => Cast(options),
                "wifi" => await WifiAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "hzdb" => await HzdbAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "media" => await MediaAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "tooling" => await ToolingAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "catalog" => await CatalogAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                _ => Fail($"Unknown command '{command}'. Run --help for commands.")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int Version(ArgOptions options)
    {
        var identity = AppBuildIdentity.Detect();
        if (options.Has("--json"))
        {
            WriteObject(identity, json: true);
        }
        else
        {
            Console.WriteLine(identity.DisplayLabel);
        }

        return 0;
    }

    private static async Task<int> DoctorAsync(ArgOptions options)
    {
        var analyzer = new WindowsEnvironmentAnalyzer();
        var report = await analyzer.AnalyzeAsync(options.Has("--snapshots")).ConfigureAwait(false);
        if (options.Has("--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        }
        else
        {
            Console.WriteLine(DiagnosticsBundleWriter.ToMarkdown(report));
        }

        if (options.TryGet("--out", out var outputRoot))
        {
            var folder = await new DiagnosticsBundleWriter().WriteAsync(report, outputRoot).ConfigureAwait(false);
            Console.Error.WriteLine($"Diagnostics bundle written to {folder}");
        }

        return 0;
    }

    private static async Task<int> DevicesAsync(ArgOptions options)
    {
        var devices = await new QuestAdbService().ListDevicesAsync().ConfigureAwait(false);
        WriteObject(devices, options.Has("--json"));
        return devices.Any(static device => device.IsOnline) ? 0 : 2;
    }

    private static async Task<int> ConnectAsync(ArgOptions options)
    {
        var endpointText = Required(options, "--endpoint");
        if (!QuestEndpoint.TryParse(endpointText, out var endpoint))
        {
            return Fail($"Invalid endpoint '{endpointText}'. Use host:port or host.");
        }

        var result = await new QuestAdbService().ConnectAsync(endpoint).ConfigureAwait(false);
        WriteCommandResult(result);
        return result.Succeeded ? 0 : result.ExitCode;
    }

    private static async Task<int> SnapshotAsync(ArgOptions options)
    {
        var snapshot = await new QuestAdbService().GetSnapshotAsync(Required(options, "--serial")).ConfigureAwait(false);
        WriteObject(snapshot, options.Has("--json"));
        return 0;
    }

    private static async Task<int> InstallAsync(ArgOptions options)
    {
        var result = await new QuestAdbService()
            .InstallAsync(Required(options, "--serial"), Required(options, "--apk"))
            .ConfigureAwait(false);
        WriteCommandResult(result);
        return result.Succeeded ? 0 : result.ExitCode;
    }

    private static async Task<int> LaunchAsync(ArgOptions options)
    {
        var result = await new QuestAdbService()
            .LaunchAsync(Required(options, "--serial"), Required(options, "--package"), options.ValueOrNull("--activity"))
            .ConfigureAwait(false);
        WriteCommandResult(result);
        return result.Succeeded ? 0 : result.ExitCode;
    }

    private static async Task<int> StopAsync(ArgOptions options)
    {
        var result = await new QuestAdbService()
            .StopAsync(Required(options, "--serial"), Required(options, "--package"))
            .ConfigureAwait(false);
        WriteCommandResult(result);
        return result.Succeeded ? 0 : result.ExitCode;
    }

    private static async Task<int> ProfileAsync(string[] args)
    {
        if (args.Length == 0 || args[0] != "apply")
        {
            return Fail("Use: profile apply --serial <serial> [--cpu <level>] [--gpu <level>] [--prop key=value]");
        }

        var options = ArgOptions.Parse(args.Skip(1));
        var properties = options.Values("--prop")
            .Select(static value => value.Split('=', 2))
            .Where(static parts => parts.Length == 2 && parts[0].Length > 0)
            .ToDictionary(static parts => parts[0], static parts => parts[1], StringComparer.Ordinal);

        int? cpu = options.TryGet("--cpu", out var cpuText) ? int.Parse(cpuText) : null;
        int? gpu = options.TryGet("--gpu", out var gpuText) ? int.Parse(gpuText) : null;

        var results = await new QuestAdbService()
            .ApplyDeviceProfileAsync(Required(options, "--serial"), cpu, gpu, properties)
            .ConfigureAwait(false);

        foreach (var result in results)
        {
            WriteCommandResult(result);
        }

        return results.All(static result => result.Succeeded) ? 0 : 1;
    }

    private static int Cast(ArgOptions options)
    {
        int? maxSize = options.TryGet("--max-size", out var maxSizeText) ? int.Parse(maxSizeText) : null;
        int? bitrate = options.TryGet("--bitrate-mbps", out var bitrateText) ? int.Parse(bitrateText) : null;
        var session = new ScrcpyService().Start(new StreamLaunchRequest(Required(options, "--serial"), maxSize, bitrate));
        Console.WriteLine($"Started scrcpy process {session.ProcessId}: {session.ToolPath} {session.Arguments}");
        return 0;
    }

    private static async Task<int> WifiAsync(string[] args)
    {
        var subcommand = args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal)
            ? "enable"
            : args[0].ToLowerInvariant();
        var optionArgs = subcommand == "enable" && args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
            ? args.Skip(1)
            : args;
        var options = ArgOptions.Parse(optionArgs);

        if (subcommand != "enable")
        {
            return Fail("Use: wifi enable --serial <usb-serial> [--port <port>] [--json]");
        }

        var port = options.TryGet("--port", out var portText)
            ? int.Parse(portText)
            : QuestEndpoint.DefaultAdbPort;
        var result = await new QuestAdbService()
            .EnableWifiAdbAsync(Required(options, "--serial"), port)
            .ConfigureAwait(false);

        WriteObject(result, options.Has("--json"));
        return result.Succeeded ? 0 : 2;
    }

    private static async Task<int> HzdbAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("Use: hzdb <status|proximity|wake|screenshot|info> --serial <serial>");
        }

        var subcommand = args[0].ToLowerInvariant();
        var options = ArgOptions.Parse(args.Skip(1));
        var hzdb = new HzdbService();

        return subcommand switch
        {
            "status" => await HzdbStatusAsync(hzdb, options).ConfigureAwait(false),
            "proximity" => await HzdbProximityAsync(hzdb, args.Skip(1).ToArray()).ConfigureAwait(false),
            "wake" => await HzdbWakeAsync(hzdb, options).ConfigureAwait(false),
            "screenshot" => await HzdbScreenshotAsync(hzdb, options).ConfigureAwait(false),
            "info" => await HzdbInfoAsync(hzdb, options).ConfigureAwait(false),
            _ => Fail("Use: hzdb <status|proximity|wake|screenshot|info> --serial <serial>")
        };
    }

    private static async Task<int> HzdbStatusAsync(HzdbService hzdb, ArgOptions options)
    {
        var status = await hzdb.GetProximityStatusAsync(Required(options, "--serial")).ConfigureAwait(false);
        WriteObject(status, options.Has("--json"));
        return status.Available ? 0 : 2;
    }

    private static async Task<int> HzdbProximityAsync(HzdbService hzdb, string[] args)
    {
        var mode = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
            ? args[0]
            : null;
        var options = ArgOptions.Parse(mode is null ? args : args.Skip(1));
        mode ??= options.ValueOrNull("--mode") ?? "keep-awake";

        var enableNormalProximity = mode.ToLowerInvariant() switch
        {
            "normal" or "enable" or "enabled" or "on" => true,
            "keep-awake" or "disable" or "disabled" or "off" => false,
            _ => throw new ArgumentException("Proximity mode must be keep-awake or normal.")
        };

        int? durationMs = options.TryGet("--duration-ms", out var durationText) ? int.Parse(durationText) : 28_800_000;
        if (enableNormalProximity)
        {
            durationMs = null;
        }

        var result = await hzdb
            .SetProximityAsync(Required(options, "--serial"), enableNormalProximity, durationMs)
            .ConfigureAwait(false);
        WriteCommandResult(result);

        var status = await hzdb.GetProximityStatusAsync(Required(options, "--serial")).ConfigureAwait(false);
        Console.WriteLine(status.Detail);
        return result.Succeeded ? 0 : result.ExitCode;
    }

    private static async Task<int> HzdbWakeAsync(HzdbService hzdb, ArgOptions options)
    {
        var result = await hzdb.WakeDeviceAsync(Required(options, "--serial")).ConfigureAwait(false);
        WriteCommandResult(result);
        return result.Succeeded ? 0 : result.ExitCode;
    }

    private static async Task<int> HzdbInfoAsync(HzdbService hzdb, ArgOptions options)
    {
        var result = await hzdb.GetDeviceInfoAsync(Required(options, "--serial")).ConfigureAwait(false);
        WriteCommandResult(result);
        return result.Succeeded ? 0 : result.ExitCode;
    }

    private static async Task<int> HzdbScreenshotAsync(HzdbService hzdb, ArgOptions options)
    {
        var serial = Required(options, "--serial");
        var output = options.ValueOrNull("--out") ??
                     Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "RustyXrCompanion",
                         "screenshots");
        var outputPath = output.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? output
            : HzdbService.CreateDefaultScreenshotPath(output, serial);
        var capture = await hzdb
            .CaptureScreenshotAsync(serial, outputPath, options.ValueOrNull("--method"))
            .ConfigureAwait(false);

        WriteObject(capture, options.Has("--json"));
        return capture.Succeeded ? 0 : 2;
    }

    private static async Task<int> MediaAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("Use: media <reverse|receive> [options]");
        }

        var subcommand = args[0].ToLowerInvariant();
        var options = ArgOptions.Parse(args.Skip(1));
        switch (subcommand)
        {
            case "reverse":
            {
                var devicePort = options.TryGet("--device-port", out var devicePortText)
                    ? int.Parse(devicePortText)
                    : MediaFrameReceiverService.DefaultPort;
                var hostPort = options.TryGet("--host-port", out var hostPortText)
                    ? int.Parse(hostPortText)
                    : MediaFrameReceiverService.DefaultPort;
                var result = await new QuestAdbService()
                    .ReverseTcpAsync(Required(options, "--serial"), devicePort, hostPort)
                    .ConfigureAwait(false);
                WriteCommandResult(result);
                return result.Succeeded ? 0 : result.ExitCode;
            }

            case "receive":
            {
                var host = options.ValueOrNull("--host") ?? "127.0.0.1";
                var port = options.TryGet("--port", out var portText)
                    ? int.Parse(portText)
                    : MediaFrameReceiverService.DefaultPort;
                var output = options.ValueOrNull("--out") ??
                             Path.Combine(
                                 Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                 "RustyXrCompanion",
                                 "media-stream");
                using var timeout = new CancellationTokenSource();
                if (options.TryGet("--timeout-ms", out var timeoutText))
                {
                    timeout.CancelAfter(int.Parse(timeoutText));
                }

                var result = await new MediaFrameReceiverService()
                    .ReceiveAsync(host, port, output, options.Has("--once"), timeout.Token)
                    .ConfigureAwait(false);
                WriteObject(result, options.Has("--json"));
                return 0;
            }

            default:
                return Fail("Use: media <reverse|receive> [options]");
        }
    }

    private static async Task<int> ToolingAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("Use: tooling <status|install-official> [--json]");
        }

        using var service = new OfficialQuestToolingService();
        var subcommand = args[0].ToLowerInvariant();
        var options = ArgOptions.Parse(args.Skip(1));
        switch (subcommand)
        {
            case "status":
            {
                var status = options.Has("--latest")
                    ? await service.GetStatusAsync().ConfigureAwait(false)
                    : service.GetLocalStatus();
                WriteObject(status, options.Has("--json"));
                return status.IsReady ? 0 : 2;
            }

            case "install-official":
            {
                var progress = new InlineProgress<OfficialQuestToolingProgress>(item =>
                    Console.Error.WriteLine($"[{item.PercentComplete,3}%] {item.Status}: {item.Detail}"));
                var result = await service.InstallOrUpdateAsync(progress).ConfigureAwait(false);
                WriteObject(result, options.Has("--json"));
                return result.Status.IsReady ? 0 : 2;
            }

            default:
                return Fail("Use: tooling <status|install-official> [--json]");
        }
    }

    private static async Task<int> CatalogAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("Use: catalog <list|install|launch|stop|verify> --path <catalog.json>");
        }

        return args[0] switch
        {
            "list" => await CatalogListAsync(ArgOptions.Parse(args.Skip(1))).ConfigureAwait(false),
            "install" => await CatalogInstallAsync(ArgOptions.Parse(args.Skip(1))).ConfigureAwait(false),
            "launch" => await CatalogLaunchAsync(ArgOptions.Parse(args.Skip(1))).ConfigureAwait(false),
            "stop" => await CatalogStopAsync(ArgOptions.Parse(args.Skip(1))).ConfigureAwait(false),
            "verify" => await CatalogVerifyAsync(ArgOptions.Parse(args.Skip(1))).ConfigureAwait(false),
            _ => Fail("Use: catalog <list|install|launch|stop|verify> --path <catalog.json>")
        };
    }

    private static async Task<int> CatalogListAsync(ArgOptions options)
    {
        var path = CatalogPath(options);
        var catalog = await new CatalogLoader().LoadAsync(path).ConfigureAwait(false);
        WriteObject(catalog, options.Has("--json"));
        return 0;
    }

    private static async Task<int> CatalogInstallAsync(ArgOptions options)
    {
        var selection = await CatalogSelectionAsync(options).ConfigureAwait(false);
        var apkSource = options.ValueOrNull("--apk") ?? selection.ResolvedApkPath;
        if (string.IsNullOrWhiteSpace(apkSource))
        {
            return Fail("Catalog app has no apkFile. Pass --apk <path>.");
        }

        var apkPath = await ResolveApkSourceAsync(apkSource, options).ConfigureAwait(false);
        var result = await new QuestAdbService()
            .InstallAsync(Required(options, "--serial"), apkPath)
            .ConfigureAwait(false);
        WriteCommandResult(result);
        return result.Succeeded ? 0 : result.ExitCode;
    }

    private static async Task<int> CatalogLaunchAsync(ArgOptions options)
    {
        var selection = await CatalogSelectionAsync(options).ConfigureAwait(false);
        var runtimeProfile = ResolveRuntimeProfile(selection.Catalog, options.ValueOrNull("--runtime-profile"));
        var result = await new QuestAdbService()
            .LaunchAsync(Required(options, "--serial"), selection.App.PackageName, selection.App.ActivityName, runtimeProfile?.Values)
            .ConfigureAwait(false);
        WriteCommandResult(result);
        return result.Succeeded ? 0 : result.ExitCode;
    }

    private static async Task<int> CatalogStopAsync(ArgOptions options)
    {
        var selection = await CatalogSelectionAsync(options).ConfigureAwait(false);
        var result = await new QuestAdbService()
            .StopAsync(Required(options, "--serial"), selection.App.PackageName)
            .ConfigureAwait(false);
        WriteCommandResult(result);
        return result.Succeeded ? 0 : result.ExitCode;
    }

    private static async Task<int> CatalogVerifyAsync(ArgOptions options)
    {
        var serial = Required(options, "--serial");
        var selection = await CatalogSelectionAsync(options).ConfigureAwait(false);
        var adb = new QuestAdbService();
        var commands = new List<CommandResult>();
        var notes = new List<string>();
        var deviceProfileId = options.ValueOrNull("--device-profile");
        var runtimeProfileId = options.ValueOrNull("--runtime-profile");
        var runtimeProfile = ResolveRuntimeProfile(selection.Catalog, runtimeProfileId);
        var hasOutputRoot = options.TryGet("--out", out var outputRoot);
        var logcatLines = options.TryGet("--logcat-lines", out var logcatLinesText)
            ? int.Parse(logcatLinesText)
            : 0;
        CancellationTokenSource? mediaReceiverCancellation = null;
        Task<MediaReceiverResult>? mediaReceiverTask = null;
        var mediaReceiverOutput = options.ValueOrNull("--media-out") ??
                                  Path.Combine(
                                      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                      "RustyXrCompanion",
                                      "media-stream");

        var before = await adb.GetSnapshotAsync(serial).ConfigureAwait(false);

        if (options.Has("--stop-catalog-apps"))
        {
            var packages = selection.Catalog.Apps
                .Select(static app => app.PackageName)
                .Where(static packageName => !string.IsNullOrWhiteSpace(packageName))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            foreach (var packageName in packages)
            {
                commands.Add(await adb.StopAsync(serial, packageName).ConfigureAwait(false));
            }

            notes.Add($"Stopped {packages.Length} catalog package(s) before verification.");
        }
        else if (!options.Has("--no-stop-target") && (options.Has("--install") || options.Has("--launch")))
        {
            commands.Add(await adb.StopAsync(serial, selection.App.PackageName).ConfigureAwait(false));
            notes.Add("Stopped the target package before verification.");
        }

        if (logcatLines > 0 && !options.Has("--keep-logcat"))
        {
            commands.Add(await adb.ClearLogcatAsync(serial).ConfigureAwait(false));
            notes.Add("Cleared device logcat before verification launch.");
        }

        if (options.Has("--media-receiver"))
        {
            var mediaPort = options.TryGet("--media-port", out var mediaPortText)
                ? int.Parse(mediaPortText)
                : MediaFrameReceiverService.DefaultPort;
            var mediaTimeoutMs = options.TryGet("--receiver-timeout-ms", out var receiverTimeoutText)
                ? int.Parse(receiverTimeoutText)
                : 15_000;
            mediaReceiverCancellation = new CancellationTokenSource(mediaTimeoutMs);
            mediaReceiverTask = new MediaFrameReceiverService().ReceiveAsync(
                "127.0.0.1",
                mediaPort,
                mediaReceiverOutput,
                once: true,
                mediaReceiverCancellation.Token);
            await Task.Delay(250).ConfigureAwait(false);
            commands.Add(await adb.ReverseTcpAsync(serial, mediaPort, mediaPort).ConfigureAwait(false));
            notes.Add($"Media receiver armed on 127.0.0.1:{mediaPort}; output `{mediaReceiverOutput}`.");
        }

        if (!string.IsNullOrWhiteSpace(deviceProfileId))
        {
            var profile = selection.Catalog.DeviceProfiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, deviceProfileId, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                return Fail($"Device profile '{deviceProfileId}' was not found.");
            }

            var properties = profile.Properties.ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal);
            commands.AddRange(await adb.ApplyDeviceProfileAsync(serial, null, null, properties).ConfigureAwait(false));
        }

        if (options.Has("--install"))
        {
            var apkSource = options.ValueOrNull("--apk") ?? selection.ResolvedApkPath;
            if (string.IsNullOrWhiteSpace(apkSource))
            {
                return Fail("Catalog app has no apkFile. Pass --apk <path>.");
            }

            var apkPath = await ResolveApkSourceAsync(apkSource, options).ConfigureAwait(false);
            if (!string.Equals(apkPath, apkSource, StringComparison.Ordinal))
            {
                notes.Add($"Downloaded APK source to `{apkPath}`.");
            }

            commands.Add(await adb.InstallAsync(serial, apkPath).ConfigureAwait(false));
        }

        if (options.Has("--launch"))
        {
            commands.Add(await adb.LaunchAsync(
                serial,
                selection.App.PackageName,
                selection.App.ActivityName,
                runtimeProfile?.Values).ConfigureAwait(false));
        }

        var settleMs = options.TryGet("--settle-ms", out var settleText) && int.TryParse(settleText, out var parsedSettle)
            ? parsedSettle
            : 2500;
        if (settleMs > 0)
        {
            await Task.Delay(settleMs).ConfigureAwait(false);
        }

        var after = await adb.GetSnapshotAsync(serial).ConfigureAwait(false);
        var diagnostics = await adb.GetAppDiagnosticsAsync(serial, selection.App.PackageName).ConfigureAwait(false);
        if (mediaReceiverTask is not null)
        {
            if (!mediaReceiverTask.IsCompleted)
            {
                mediaReceiverCancellation?.Cancel();
            }

            try
            {
                var mediaResult = await mediaReceiverTask.ConfigureAwait(false);
                notes.Add($"Media receiver captured {mediaResult.FrameCount} frame(s) under `{mediaResult.OutputDirectory}`.");
            }
            catch (OperationCanceledException)
            {
                notes.Add("Media receiver timed out before a frame arrived.");
            }
            catch (Exception exception)
            {
                notes.Add($"Media receiver failed: {exception.Message}");
            }
            finally
            {
                mediaReceiverCancellation?.Dispose();
            }
        }

        if (!diagnostics.ProcessRunning)
        {
            notes.Add("Target process was not running when diagnostics were captured.");
        }

        if (!diagnostics.ForegroundMatchesPackage)
        {
            notes.Add("Foreground activity did not match the catalog package.");
        }

        var logcatText = logcatLines > 0
            ? await adb.DumpLogcatAsync(serial, logcatLines).ConfigureAwait(false)
            : null;
        if (logcatLines > 0)
        {
            notes.Add(hasOutputRoot
                ? "Logcat capture requested; saved as logcat.txt in the verification bundle."
                : "Logcat capture requested; pass --out to save logcat.txt with the verification bundle.");
        }
        var cameraSourceDiagnosticsJson = CameraSourceDiagnosticsLogExtractor.TryExtract(logcatText, out var extractedDiagnostics)
            ? extractedDiagnostics
            : null;
        if (ShouldPullCameraSourceDiagnostics(runtimeProfileId, logcatText))
        {
            var pull = await adb
                .ReadRunAsTextFileAsync(
                    serial,
                    selection.App.PackageName,
                    "files/camera-source-diagnostics.json")
                .ConfigureAwait(false);
            var pulledJson = pull.StandardOutput.Trim();
            if (pull.Succeeded && IsJsonObject(pulledJson))
            {
                cameraSourceDiagnosticsJson = pulledJson;
                notes.Add(hasOutputRoot
                    ? "Camera source diagnostics JSON pulled from the app-private file as camera-source-diagnostics.json."
                    : "Camera source diagnostics JSON was pulled from the app-private file; pass --out to save it.");
            }
            else
            {
                notes.Add($"Camera source diagnostics file pull failed or was not JSON: {pull.CondensedOutput}");
            }
        }
        if (cameraSourceDiagnosticsJson is not null)
        {
            notes.Add(hasOutputRoot
                ? "Camera source diagnostics JSON captured as camera-source-diagnostics.json."
                : "Camera source diagnostics JSON was present in logcat; pass --out to save it.");
        }

        var profileLogValidationSucceeded = true;
        if (RuntimeProfileLogValidator.RequiresAlignedGpuProjection(runtimeProfile))
        {
            var profileLogValidation = RuntimeProfileLogValidator.Validate(runtimeProfile, logcatText);
            profileLogValidationSucceeded = profileLogValidation.Succeeded;
            notes.Add(profileLogValidation.Detail);
        }

        var report = new CatalogVerificationReport(
            DateTimeOffset.Now,
            selection.CatalogPath,
            selection.App,
            selection.ResolvedApkPath,
            deviceProfileId,
            runtimeProfileId,
            before,
            after,
            diagnostics,
            commands,
            notes);

        if (hasOutputRoot)
        {
            var folder = WriteVerificationBundle(report, outputRoot, logcatText, cameraSourceDiagnosticsJson);
            Console.Error.WriteLine($"Verification bundle written to {folder}");
        }

        WriteObject(report, options.Has("--json"));
        return commands.All(static command => command.Succeeded) && diagnostics.ProcessRunning && profileLogValidationSucceeded ? 0 : 2;
    }

    private static bool ShouldPullCameraSourceDiagnostics(string? runtimeProfileId, string? logcatText) =>
        string.Equals(runtimeProfileId, "camera-source-diagnostics", StringComparison.OrdinalIgnoreCase) ||
        (logcatText?.Contains("Rusty XR camera source diagnostics ready file=files/camera-source-diagnostics.json", StringComparison.Ordinal) ?? false);

    private static bool IsJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string CatalogPath(ArgOptions options) =>
        options.ValueOrNull("--path") ?? CompanionContentLayout.DefaultOrFallbackCatalogPath();

    private static Task<CatalogAppSelection> CatalogSelectionAsync(ArgOptions options) =>
        new CatalogLoader().SelectAppAsync(CatalogPath(options), Required(options, "--app"));

    private static RuntimeProfile? ResolveRuntimeProfile(QuestSessionCatalog catalog, string? runtimeProfileId)
    {
        if (string.IsNullOrWhiteSpace(runtimeProfileId))
        {
            return null;
        }

        var profile = catalog.RuntimeProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, runtimeProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            throw new InvalidOperationException($"Runtime profile '{runtimeProfileId}' was not found.");
        }

        return profile;
    }

    private static async Task<string> ResolveApkSourceAsync(string apkSource, ArgOptions options)
    {
        if (!Uri.TryCreate(apkSource, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return apkSource;
        }

        var cacheRoot = options.ValueOrNull("--apk-cache") ??
                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "RustyXrCompanion",
                            "apk-cache");
        Directory.CreateDirectory(cacheRoot);

        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName) ||
            !fileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
        {
            fileName = "catalog-app.apk";
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..12]
            .ToLowerInvariant();
        var outputPath = Path.Combine(cacheRoot, $"{name}-{hash}.apk");
        if (File.Exists(outputPath) && !options.Has("--refresh-apk-download"))
        {
            return outputPath;
        }

        var tempPath = outputPath + ".tmp";
        using var http = new HttpClient();
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
        await using (var output = File.Create(tempPath))
        {
            await input.CopyToAsync(output).ConfigureAwait(false);
        }

        File.Move(tempPath, outputPath, overwrite: true);
        return outputPath;
    }

    private static string WriteVerificationBundle(
        CatalogVerificationReport report,
        string outputRoot,
        string? logcatText = null,
        string? cameraSourceDiagnosticsJson = null)
    {
        var folder = Path.Combine(outputRoot, $"verify-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "verification.json"), JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(Path.Combine(folder, "verification.md"), ToMarkdown(report));
        if (!string.IsNullOrWhiteSpace(logcatText))
        {
            File.WriteAllText(Path.Combine(folder, "logcat.txt"), logcatText);
        }
        if (!string.IsNullOrWhiteSpace(cameraSourceDiagnosticsJson))
        {
            File.WriteAllText(Path.Combine(folder, "camera-source-diagnostics.json"), cameraSourceDiagnosticsJson);
        }

        return folder;
    }

    private static string ToMarkdown(CatalogVerificationReport report)
    {
        var lines = new List<string>
        {
            "# Rusty XR Catalog Verification",
            string.Empty,
            $"Captured: `{report.CapturedAt:O}`",
            $"Catalog: `{report.CatalogPath}`",
            $"App: `{report.App.Label}` (`{report.App.Id}`)",
            $"Package: `{report.App.PackageName}`",
            $"Activity: `{report.App.ActivityName ?? "launcher"}`",
            $"APK: `{report.ResolvedApkPath ?? "none"}`",
            $"Device profile: `{report.DeviceProfileId ?? "none"}`",
            $"Runtime profile: `{report.RuntimeProfileId ?? "none"}`",
            string.Empty,
            "## Snapshots",
            string.Empty,
            $"Before: `{report.BeforeSnapshot.Foreground}` / `{report.BeforeSnapshot.Wakefulness}`",
            $"After: `{report.AfterSnapshot.Foreground}` / `{report.AfterSnapshot.Wakefulness}`",
            string.Empty,
            "## App Diagnostics",
            string.Empty,
            $"- Process running: `{report.Diagnostics.ProcessRunning}`",
            $"- PID: `{report.Diagnostics.ProcessId ?? "none"}`",
            $"- Foreground matches package: `{report.Diagnostics.ForegroundMatchesPackage}`",
            $"- Foreground: `{report.Diagnostics.Foreground}`",
            $"- Gfx: `{report.Diagnostics.GfxInfoSummary}`",
            $"- Memory: `{report.Diagnostics.MemorySummary}`",
            string.Empty,
            "## Commands",
            string.Empty
        };

        lines.AddRange(report.Commands.Select(static command =>
            $"- `{command.FileName} {command.Arguments}` -> `{command.ExitCode}`"));

        if (report.Notes.Count > 0)
        {
            lines.AddRange(new[] { string.Empty, "## Notes", string.Empty });
            lines.AddRange(report.Notes.Select(static note => $"- {note}"));
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static void WriteObject<T>(T value, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
            return;
        }

        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }

    private static void WriteCommandResult(CommandResult result)
    {
        Console.WriteLine($"> {result.FileName} {result.Arguments}");
        Console.WriteLine($"Exit: {result.ExitCode} Duration: {result.Duration}");
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            Console.WriteLine(result.StandardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            Console.Error.WriteLine(result.StandardError.TrimEnd());
        }
    }

    private static string Required(ArgOptions options, string name) =>
        options.TryGet(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{name} is required.");

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static void WriteHelp()
    {
        Console.WriteLine("""
        Rusty XR Companion CLI

        Commands:
          version [--json]
          doctor [--snapshots] [--json] [--out <folder>]
          devices [--json]
          connect --endpoint <host[:port]>
          snapshot --serial <serial> [--json]
          install --serial <serial> --apk <path>
          launch --serial <serial> --package <package> [--activity <activity>]
          stop --serial <serial> --package <package>
          profile apply --serial <serial> [--cpu <level>] [--gpu <level>] [--prop key=value]
          cast --serial <serial> [--max-size <pixels>] [--bitrate-mbps <n>]
          wifi enable --serial <usb-serial> [--port <port>] [--json]
          hzdb status --serial <serial> [--json]
          hzdb proximity <keep-awake|normal> --serial <serial> [--duration-ms <n>]
          hzdb wake --serial <serial>
          hzdb screenshot --serial <serial> [--method <screencap|metacam>] [--out <folder-or-png>] [--json]
          media reverse --serial <serial> [--device-port <n>] [--host-port <n>]
          media receive [--host 127.0.0.1] [--port <n>] [--out <folder>] [--once] [--timeout-ms <n>] [--json]
          tooling status [--latest] [--json]
          tooling install-official [--json]
          catalog list [--path <catalog.json>] [--json]
          catalog install --path <catalog.json> --app <id> --serial <serial> [--apk <path-or-url>] [--apk-cache <folder>] [--refresh-apk-download]
          catalog launch --path <catalog.json> --app <id> --serial <serial> [--runtime-profile <id>]
          catalog stop --path <catalog.json> --app <id> --serial <serial>
          catalog verify --path <catalog.json> --app <id> --serial <serial> [--install] [--launch] [--apk <path-or-url>] [--apk-cache <folder>] [--refresh-apk-download] [--device-profile <id>] [--runtime-profile <id>] [--settle-ms <n>] [--media-receiver] [--media-port <n>] [--media-out <folder>] [--receiver-timeout-ms <n>] [--stop-catalog-apps] [--no-stop-target] [--logcat-lines <n>] [--keep-logcat] [--out <folder>] [--json]
        """);
    }

    private sealed record CatalogVerificationReport(
        DateTimeOffset CapturedAt,
        string CatalogPath,
        QuestAppTarget App,
        string? ResolvedApkPath,
        string? DeviceProfileId,
        string? RuntimeProfileId,
        QuestSnapshot BeforeSnapshot,
        QuestSnapshot AfterSnapshot,
        QuestAppDiagnostics Diagnostics,
        IReadOnlyList<CommandResult> Commands,
        IReadOnlyList<string> Notes);

    private sealed class ArgOptions
    {
        private readonly Dictionary<string, List<string>> _values;

        private ArgOptions(Dictionary<string, List<string>> values)
        {
            _values = values;
        }

        public static ArgOptions Parse(IEnumerable<string> args)
        {
            var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string? current = null;
            foreach (var arg in args)
            {
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    current = arg;
                    if (!values.ContainsKey(current))
                    {
                        values[current] = new List<string>();
                    }
                    continue;
                }

                if (current is null)
                {
                    continue;
                }

                values[current].Add(arg);
            }

            return new ArgOptions(values);
        }

        public bool Has(string key) => _values.ContainsKey(key);
        public bool TryGet(string key, out string value)
        {
            value = string.Empty;
            if (!_values.TryGetValue(key, out var values) || values.Count == 0)
            {
                return false;
            }

            value = values[^1];
            return true;
        }

        public string? ValueOrNull(string key) => TryGet(key, out var value) ? value : null;
        public IReadOnlyList<string> Values(string key) => _values.TryGetValue(key, out var values) ? values : Array.Empty<string>();
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public InlineProgress(Action<T> report)
        {
            _report = report;
        }

        public void Report(T value) => _report(value);
    }
}
