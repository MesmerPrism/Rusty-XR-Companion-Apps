using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
                "status" => await SnapshotAsync(options).ConfigureAwait(false),
                "snapshot" => await SnapshotAsync(options).ConfigureAwait(false),
                "install" => await InstallAsync(options).ConfigureAwait(false),
                "launch" => await LaunchAsync(options).ConfigureAwait(false),
                "stop" => await StopAsync(options).ConfigureAwait(false),
                "profile" => await ProfileAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "cast" => Cast(options),
                "wifi" => await WifiAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "hzdb" => await HzdbAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "media" => await MediaAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "osc" => await OscAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "lsl" => await LslAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "broker" => await BrokerAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "tooling" => await ToolingAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "catalog" => await CatalogAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "workspace" => Workspace(args.Skip(1).ToArray()),
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
            return Fail("Use: media <reverse|receive|inspect-h264|inspect-raw-luma> [options]");
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

            case "inspect-h264":
                {
                    var runDecoderProbe = options.Has("--decode") || options.TryGet("--ffmpeg", out _);
                    var report = await new EncodedVideoArtifactInspectionService()
                        .InspectAsync(new EncodedVideoArtifactInspectionOptions(
                            Required(options, "--payload"),
                            RunDecoderProbe: runDecoderProbe,
                            FfmpegPath: options.ValueOrNull("--ffmpeg") ?? "ffmpeg",
                            DecoderProbeTimeoutMilliseconds: ParseInt(options, "--timeout-ms", 10000)))
                        .ConfigureAwait(false);
                    WriteObject(report, options.Has("--json"));
                    if (report.DecoderProbeRequested)
                    {
                        return report.DecoderProbeSucceeded ? 0 : 2;
                    }

                    return report.HasInspectableH264Structure ? 0 : 2;
                }

            case "inspect-raw-luma":
                {
                    var report = await new RawLumaArtifactInspectionService()
                        .InspectAsync(new RawLumaArtifactInspectionOptions(
                            Required(options, "--payload"),
                            Width: ParseInt(options, "--width", 0),
                            Height: ParseInt(options, "--height", 0),
                            ContactSheetPath: options.ValueOrNull("--contact-sheet"),
                            MaxContactSheetFrames: ParseInt(options, "--max-frames", 8)))
                        .ConfigureAwait(false);
                    WriteObject(report, options.Has("--json"));
                    return report.HasCompleteFrames && report.IsFrameAligned ? 0 : 2;
                }

            default:
                return Fail("Use: media <reverse|receive|inspect-h264|inspect-raw-luma> [options]");
        }
    }

    private static async Task<int> OscAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("Use: osc <send|receive> [options]");
        }

        var subcommand = args[0].ToLowerInvariant();
        var options = ArgOptions.Parse(args.Skip(1));
        var service = new OscService();
        switch (subcommand)
        {
            case "send":
                {
                    var host = options.ValueOrNull("--host") ?? "127.0.0.1";
                    var port = options.TryGet("--port", out var portText)
                        ? int.Parse(portText, CultureInfo.InvariantCulture)
                        : OscService.DefaultPort;
                    var address = options.ValueOrNull("--address") ?? "/rusty-xr/probe";
                    var arguments = options.Values("--arg").Select(ParseOscArgument).ToArray();
                    if (arguments.Length == 0)
                    {
                        arguments = [OscArgument.String("hello")];
                    }

                    var result = await service
                        .SendAsync(host, port, new OscMessage(address, arguments))
                        .ConfigureAwait(false);
                    WriteObject(result, options.Has("--json"));
                    return 0;
                }

            case "receive":
                {
                    var host = options.ValueOrNull("--host") ?? "0.0.0.0";
                    var port = options.TryGet("--port", out var portText)
                        ? int.Parse(portText, CultureInfo.InvariantCulture)
                        : OscService.DefaultPort;
                    var count = options.TryGet("--count", out var countText)
                        ? int.Parse(countText, CultureInfo.InvariantCulture)
                        : 1;
                    var maxPacketBytes = options.TryGet("--max-packet-bytes", out var maxPacketText)
                        ? int.Parse(maxPacketText, CultureInfo.InvariantCulture)
                        : OscService.DefaultMaxPacketBytes;
                    using var timeout = new CancellationTokenSource();
                    if (options.TryGet("--timeout-ms", out var timeoutText))
                    {
                        timeout.CancelAfter(int.Parse(timeoutText, CultureInfo.InvariantCulture));
                    }

                    var result = await service
                        .ReceiveAsync(host, port, count, maxPacketBytes, timeout.Token)
                        .ConfigureAwait(false);
                    WriteObject(result, options.Has("--json"));
                    return 0;
                }

            default:
                return Fail("Use: osc <send|receive> [options]");
        }
    }

    private static async Task<int> BrokerAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("Use: broker <forward|status|command|capabilities|streams|subscribe|unsubscribe|sample|verify|compare|bio-simulate|app-camera-luma-probe|app-camera-h264-probe|app-camera-h264-decode-probe|shell-helper> [options]");
        }

        var subcommand = args[0].ToLowerInvariant();
        if (subcommand == "shell-helper")
        {
            return await BrokerShellHelperAsync(args.Skip(1).ToArray()).ConfigureAwait(false);
        }

        var options = ArgOptions.Parse(args.Skip(1));
        return subcommand switch
        {
            "forward" => await BrokerForwardAsync(options).ConfigureAwait(false),
            "status" => await BrokerStatusAsync(options).ConfigureAwait(false),
            "command" => await BrokerCommandAsync(options.ValueOrNull("--command") ?? "status_request", options).ConfigureAwait(false),
            "capabilities" => await BrokerCommandAsync("list_capabilities", options).ConfigureAwait(false),
            "streams" => await BrokerCommandAsync("list_streams", options).ConfigureAwait(false),
            "subscribe" => await BrokerCommandAsync("subscribe", options, requireStream: true).ConfigureAwait(false),
            "unsubscribe" => await BrokerCommandAsync("unsubscribe", options, requireStream: true).ConfigureAwait(false),
            "sample" => await BrokerLatencySampleAsync(options).ConfigureAwait(false),
            "verify" => await BrokerVerifyAsync(options).ConfigureAwait(false),
            "compare" => await BrokerCompareAsync(options).ConfigureAwait(false),
            "bio-simulate" => await BrokerBioSimulateAsync(options).ConfigureAwait(false),
            "app-camera-luma-probe" => await BrokerAppCameraLumaProbeAsync(options).ConfigureAwait(false),
            "app-camera-h264-probe" => await BrokerAppCameraH264ProbeAsync(options).ConfigureAwait(false),
            "app-camera-h264-decode-probe" => await BrokerAppCameraH264DecodeProbeAsync(options).ConfigureAwait(false),
            _ => Fail("Use: broker <forward|status|command|capabilities|streams|subscribe|unsubscribe|sample|verify|compare|bio-simulate|app-camera-luma-probe|app-camera-h264-probe|app-camera-h264-decode-probe|shell-helper> [options]")
        };
    }

    private static async Task<int> LslAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("Use: lsl <runtime|loopback|broker-roundtrip> [options]");
        }

        var subcommand = args[0].ToLowerInvariant();
        var options = ArgOptions.Parse(args.Skip(1));
        return subcommand switch
        {
            "runtime" => LslRuntime(options),
            "loopback" => await LslLoopbackAsync(options).ConfigureAwait(false),
            "broker-roundtrip" => await LslBrokerRoundTripAsync(options).ConfigureAwait(false),
            _ => Fail("Use: lsl <runtime|loopback|broker-roundtrip> [options]")
        };
    }

    private static int LslRuntime(ArgOptions options)
    {
        var state = LslNativeRuntime.GetRuntimeState(options.ValueOrNull("--lsl-dll") ?? string.Empty);
        WriteObject(state, options.Has("--json"));
        return state.Available ? 0 : 2;
    }

    private static async Task<int> LslLoopbackAsync(ArgOptions options)
    {
        var report = await new LslDiagnosticsService()
            .RunLocalLoopbackAsync(new LslLocalRoundTripOptions(
                Count: ParseInt(options, "--count", 16),
                IntervalMilliseconds: ParseInt(options, "--interval-ms", 100),
                TimeoutMilliseconds: ParseInt(options, "--timeout-ms", 3000),
                ResolveTimeoutMilliseconds: ParseInt(options, "--resolve-timeout-ms", 5000),
                WarmupMilliseconds: ParseInt(options, "--warmup-ms", 300),
                StreamName: options.ValueOrNull("--stream-name") ?? string.Empty,
                StreamType: options.ValueOrNull("--stream-type") ?? LslDiagnosticDefaults.LocalLoopbackStreamType,
                SourceId: options.ValueOrNull("--source-id") ?? string.Empty,
                LslDllPath: options.ValueOrNull("--lsl-dll") ?? string.Empty))
            .ConfigureAwait(false);

        if (options.TryGet("--out", out var outputRoot))
        {
            var folder = LslDiagnosticsReportWriter.WriteLocal(report, outputRoot, includePdf: !options.Has("--no-pdf"));
            Console.Error.WriteLine($"LSL local round-trip bundle written to {folder}");
        }

        WriteObject(report, options.Has("--json"));
        return report.Succeeded ? 0 : 2;
    }

    private static async Task<int> LslBrokerRoundTripAsync(ArgOptions options)
    {
        var host = options.ValueOrNull("--host") ?? BrokerClientService.DefaultHost;
        var port = ParsePort(options, "--port", BrokerClientService.DefaultPort);
        var hostPort = ParsePort(options, "--host-port", port);
        var devicePort = ParsePort(options, "--device-port", BrokerClientService.DefaultPort);
        var notes = new List<string>();

        if (options.TryGet("--serial", out var serial))
        {
            var forwardResult = await new QuestAdbService()
                .ForwardTcpAsync(serial, hostPort, devicePort)
                .ConfigureAwait(false);
            notes.Add(forwardResult.Succeeded
                ? $"ADB forward active on tcp:{hostPort} -> tcp:{devicePort}."
                : $"ADB forward failed: {forwardResult.CondensedOutput}");
        }

        var report = await new LslDiagnosticsService()
            .RunBrokerRoundTripAsync(new LslBrokerRoundTripOptions(
                Count: ParseInt(options, "--count", 8),
                IntervalMilliseconds: ParseInt(options, "--interval-ms", 250),
                TimeoutMilliseconds: ParseInt(options, "--timeout-ms", 5000),
                ResolveTimeoutMilliseconds: ParseInt(options, "--resolve-timeout-ms", 10000),
                WarmupMilliseconds: ParseInt(options, "--warmup-ms", 500),
                SequenceStart: ParseLong(options, "--sequence-start", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                StreamName: options.ValueOrNull("--stream-name") ?? LslDiagnosticDefaults.BrokerLatencyStreamName,
                StreamType: options.ValueOrNull("--stream-type") ?? LslDiagnosticDefaults.BrokerLatencyStreamType,
                Path: options.ValueOrNull("--path") ?? "lsl_broker_roundtrip",
                PayloadSizeBytes: ParseInt(options, "--bytes", 128),
                BrokerHost: host,
                BrokerPort: hostPort,
                LslDllPath: options.ValueOrNull("--lsl-dll") ?? string.Empty))
            .ConfigureAwait(false);
        if (notes.Count > 0)
        {
            report = report with { Notes = report.Notes.Concat(notes).ToArray() };
        }

        if (options.TryGet("--out", out var outputRoot))
        {
            var folder = LslDiagnosticsReportWriter.WriteBroker(report, outputRoot, includePdf: !options.Has("--no-pdf"));
            Console.Error.WriteLine($"LSL broker round-trip bundle written to {folder}");
        }

        WriteObject(report, options.Has("--json"));
        return report.Succeeded ? 0 : 2;
    }

    private static async Task<int> BrokerForwardAsync(ArgOptions options)
    {
        var hostPort = ParsePort(options, "--host-port", BrokerClientService.DefaultPort);
        var devicePort = ParsePort(options, "--device-port", BrokerClientService.DefaultPort);
        var result = await new QuestAdbService()
            .ForwardTcpAsync(Required(options, "--serial"), hostPort, devicePort)
            .ConfigureAwait(false);

        if (options.Has("--json"))
        {
            WriteObject(new
            {
                result,
                statusUrl = BrokerClientService.CreateStatusUri(null, BrokerClientService.DefaultHost, hostPort),
                eventsUrl = BrokerClientService.CreateEventsUri(null, BrokerClientService.DefaultHost, hostPort)
            }, json: true);
        }
        else
        {
            WriteCommandResult(result);
            if (result.Succeeded)
            {
                Console.WriteLine($"Broker status: {BrokerClientService.CreateStatusUri(null, BrokerClientService.DefaultHost, hostPort)}");
                Console.WriteLine($"Broker events: {BrokerClientService.CreateEventsUri(null, BrokerClientService.DefaultHost, hostPort)}");
            }
        }

        return result.Succeeded ? 0 : result.ExitCode;
    }

    private static async Task<int> BrokerStatusAsync(ArgOptions options)
    {
        var port = ParsePort(options, "--port", BrokerClientService.DefaultPort);
        var uri = BrokerClientService.CreateStatusUri(options.ValueOrNull("--url"), options.ValueOrNull("--host"), port);
        var result = await new BrokerClientService().GetStatusAsync(uri).ConfigureAwait(false);
        WriteObject(result.Status, json: true);
        return 0;
    }

    private static async Task<int> BrokerCommandAsync(
        string command,
        ArgOptions options,
        bool requireStream = false)
    {
        var stream = options.ValueOrNull("--stream");
        if (requireStream && string.IsNullOrWhiteSpace(stream))
        {
            return Fail("--stream is required.");
        }

        var request = new BrokerCommandRequest(
            NormalizeBrokerCommand(command),
            options.ValueOrNull("--request-id") ?? $"companion-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            options.ValueOrNull("--client-id") ?? "rusty-xr-companion-cli",
            "Rusty XR Companion CLI",
            AppBuildIdentity.Detect().DisplayLabel,
            stream);
        var result = await new BrokerClientService()
            .SendCommandAsync(
                BrokerEventsUri(options),
                request,
                TimeSpan.FromMilliseconds(ParseInt(options, "--listen-ms", 0)),
                ParseInt(options, "--max-messages", 16))
            .ConfigureAwait(false);

        WriteObject(result, options.Has("--json"));
        return result.HasAcceptedAck ? 0 : 2;
    }

    private static async Task<int> BrokerLatencySampleAsync(ArgOptions options)
    {
        var sequenceId = options.TryGet("--sequence", out var sequenceText)
            ? long.Parse(sequenceText, CultureInfo.InvariantCulture)
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var request = new BrokerLatencySampleRequest(
            sequenceId,
            options.ValueOrNull("--path") ?? "companion_probe",
            ParseInt(options, "--bytes", 128),
            options.ValueOrNull("--client-id") ?? "rusty-xr-companion-cli",
            "Rusty XR Companion CLI",
            AppBuildIdentity.Detect().DisplayLabel);
        var subscribe = options.Has("--subscribe");
        var listenMs = ParseInt(options, "--listen-ms", subscribe ? 1000 : 0);
        var result = await new BrokerClientService()
            .SendLatencySampleAsync(
                BrokerEventsUri(options),
                request,
                subscribe,
                TimeSpan.FromMilliseconds(listenMs),
                ParseInt(options, "--max-messages", 16))
            .ConfigureAwait(false);

        WriteObject(result, options.Has("--json"));
        return BrokerResultHasMessageType(result, "latency_ack") ? 0 : 2;
    }

    private static async Task<int> BrokerVerifyAsync(ArgOptions options)
    {
        var host = options.ValueOrNull("--host") ?? BrokerClientService.DefaultHost;
        var port = ParsePort(options, "--port", BrokerClientService.DefaultPort);
        var hostPort = ParsePort(options, "--host-port", port);
        var devicePort = ParsePort(options, "--device-port", BrokerClientService.DefaultPort);
        var statusUri = BrokerClientService.CreateStatusUri(options.ValueOrNull("--status-url"), host, hostPort);
        var eventsUri = BrokerClientService.CreateEventsUri(options.ValueOrNull("--events-url"), host, hostPort);
        var client = new BrokerClientService();
        var notes = new List<string>();
        CommandResult? forwardResult = null;

        if (options.TryGet("--serial", out var serial))
        {
            forwardResult = await new QuestAdbService()
                .ForwardTcpAsync(serial, hostPort, devicePort)
                .ConfigureAwait(false);
            if (!forwardResult.Succeeded)
            {
                notes.Add($"ADB forward failed: {forwardResult.CondensedOutput}");
            }
        }

        var status = await client.GetStatusAsync(statusUri).ConfigureAwait(false);
        var streamsProbe = await client
            .SendCommandAsync(
                eventsUri,
                BrokerCommandRequest("list_streams", options, "verify-streams"),
                TimeSpan.Zero,
                ParseInt(options, "--max-messages", 16))
            .ConfigureAwait(false);
        var latencyProbe = await client
            .SendLatencySampleAsync(
                eventsUri,
                new BrokerLatencySampleRequest(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    options.ValueOrNull("--path") ?? "companion_verify",
                    ParseInt(options, "--bytes", 128),
                    options.ValueOrNull("--client-id") ?? "rusty-xr-companion-cli",
                    "Rusty XR Companion CLI",
                    AppBuildIdentity.Detect().DisplayLabel),
                subscribeToLatencyStream: true,
                TimeSpan.FromMilliseconds(ParseInt(options, "--listen-ms", 1000)),
                ParseInt(options, "--max-messages", 16))
            .ConfigureAwait(false);

        OscSendResult? oscSend = null;
        BrokerWebSocketProbeResult? oscProbe = null;
        var oscStream = string.Empty;
        if (options.TryGet("--osc-host", out var oscHost))
        {
            var oscAddress = options.ValueOrNull("--osc-address") ?? "/rusty-xr/drive/radius";
            oscStream = options.ValueOrNull("--osc-stream") ?? "osc:" + oscAddress;
            var oscPort = ParsePort(options, "--osc-port", OscService.DefaultPort);
            var oscValue = options.TryGet("--osc-value", out var oscValueText)
                ? float.Parse(oscValueText, CultureInfo.InvariantCulture)
                : 0.42f;
            var oscListenMs = ParseInt(options, "--osc-listen-ms", 5000);
            var oscProbeTask = client.SendCommandAsync(
                eventsUri,
                BrokerCommandRequest("subscribe", options, "verify-osc", oscStream),
                TimeSpan.FromMilliseconds(oscListenMs),
                ParseInt(options, "--max-messages", 16));

            await Task.Delay(ParseInt(options, "--osc-send-delay-ms", 500)).ConfigureAwait(false);
            oscSend = await new OscService()
                .SendAsync(oscHost, oscPort, new OscMessage(oscAddress, [OscArgument.Float(oscValue)]))
                .ConfigureAwait(false);
            oscProbe = await oscProbeTask.ConfigureAwait(false);
        }

        var statusOk = BrokerStatusContractVersion(status.Status) == "rusty.xr.broker.v1";
        var streamsOk = streamsProbe.HasAcceptedAck;
        var latencyAckOk = BrokerResultHasMessageType(latencyProbe, "latency_ack");
        var latencyStreamOk = BrokerResultHasStreamEvent(latencyProbe, "latency:sample");
        var oscOk = oscProbe is null || BrokerResultHasStreamEvent(oscProbe, oscStream);
        if (!statusOk)
        {
            notes.Add("Status response did not report contractVersion rusty.xr.broker.v1.");
        }
        if (!streamsOk)
        {
            notes.Add("Stream list command did not return an accepted command ack.");
        }
        if (!latencyAckOk || !latencyStreamOk)
        {
            notes.Add("Latency sample probe did not receive both a latency ack and latency stream event.");
        }
        if (!oscOk)
        {
            notes.Add($"OSC probe did not receive stream event {oscStream}.");
        }

        var report = new BrokerVerificationReport(
            DateTimeOffset.Now,
            statusUri,
            eventsUri,
            forwardResult,
            status.Status,
            streamsProbe,
            latencyProbe,
            oscSend,
            oscProbe,
            statusOk,
            streamsOk,
            latencyAckOk,
            latencyStreamOk,
            oscOk,
            notes);

        if (options.TryGet("--out", out var outputRoot))
        {
            var folder = WriteBrokerVerificationBundle(report, outputRoot);
            Console.Error.WriteLine($"Broker verification bundle written to {folder}");
        }

        WriteObject(report, options.Has("--json"));
        return report.Succeeded ? 0 : 2;
    }

    private static async Task<int> BrokerCompareAsync(ArgOptions options)
    {
        var questHost = options.ValueOrNull("--quest-host") ??
                        options.ValueOrNull("--osc-host") ??
                        "127.0.0.1";
        var host = options.ValueOrNull("--host") ?? BrokerClientService.DefaultHost;
        var port = ParsePort(options, "--port", BrokerClientService.DefaultPort);
        var hostPort = ParsePort(options, "--host-port", port);
        var devicePort = ParsePort(options, "--device-port", BrokerClientService.DefaultPort);
        var notes = new List<string>();

        if (options.TryGet("--serial", out var serial))
        {
            var forwardResult = await new QuestAdbService()
                .ForwardTcpAsync(serial, hostPort, devicePort)
                .ConfigureAwait(false);
            if (!forwardResult.Succeeded)
            {
                notes.Add($"ADB forward failed: {forwardResult.CondensedOutput}");
            }
            else
            {
                notes.Add($"ADB forward active on tcp:{hostPort} -> tcp:{devicePort}.");
            }
        }

        var compareOptions = new BrokerComparisonOptions(
            questHost,
            Count: ParseInt(options, "--count", 8),
            IntervalMilliseconds: ParseInt(options, "--interval-ms", 250),
            TimeoutMilliseconds: ParseInt(options, "--timeout-ms", 5000),
            IncludeDirectOsc: !options.Has("--skip-direct-osc"),
            IncludeBrokerOsc: !options.Has("--skip-broker-osc"),
            DirectOscAddress: options.ValueOrNull("--direct-osc-address") ??
                              options.ValueOrNull("--address") ??
                              BrokerDiagnosticDefaults.DriveAddress,
            DirectOscPort: ParsePort(options, "--direct-osc-port", BrokerDiagnosticDefaults.DirectOscPort),
            DirectAcknowledgementAddress: options.ValueOrNull("--ack-address") ??
                                          BrokerDiagnosticDefaults.DirectAcknowledgementAddress,
            DirectAcknowledgementPort: ParseInt(options, "--ack-port", BrokerDiagnosticDefaults.DirectAcknowledgementPort),
            BrokerOscAddress: options.ValueOrNull("--broker-osc-address") ??
                              options.ValueOrNull("--address") ??
                              BrokerDiagnosticDefaults.DriveAddress,
            BrokerOscPort: ParsePort(options, "--broker-osc-port", BrokerDiagnosticDefaults.BrokerOscPort),
            BrokerHost: host,
            BrokerPort: hostPort,
            BrokerSubscribeLeadMilliseconds: ParseInt(options, "--broker-subscribe-lead-ms", 500),
            MaxBrokerMessages: ParseInt(options, "--max-messages", 64),
            ConfigureBrokerOscIngress: !options.Has("--no-configure-broker-osc"));

        var report = await new BrokerComparisonService()
            .RunAsync(compareOptions)
            .ConfigureAwait(false);
        if (notes.Count > 0)
        {
            report = report with
            {
                Notes = report.Notes.Concat(notes).ToArray()
            };
        }

        if (options.TryGet("--out", out var outputRoot))
        {
            var folder = BrokerComparisonReportWriter.Write(report, outputRoot);
            Console.Error.WriteLine($"Broker comparison bundle written to {folder}");
        }

        WriteObject(report, options.Has("--json"));
        return report.Succeeded ? 0 : 2;
    }

    private static Uri BrokerEventsUri(ArgOptions options)
    {
        var port = ParsePort(options, "--port", BrokerClientService.DefaultPort);
        return BrokerClientService.CreateEventsUri(options.ValueOrNull("--url"), options.ValueOrNull("--host"), port);
    }

    private static async Task<int> BrokerBioSimulateAsync(ArgOptions options)
    {
        var host = options.ValueOrNull("--host") ?? BrokerClientService.DefaultHost;
        var port = ParsePort(options, "--port", BrokerClientService.DefaultPort);
        var hostPort = ParsePort(options, "--host-port", port);
        var devicePort = ParsePort(options, "--device-port", BrokerClientService.DefaultPort);
        var notes = new List<string>();

        if (options.TryGet("--serial", out var serial))
        {
            var forwardResult = await new QuestAdbService()
                .ForwardTcpAsync(serial, hostPort, devicePort)
                .ConfigureAwait(false);
            if (!forwardResult.Succeeded)
            {
                notes.Add($"ADB forward failed: {forwardResult.CondensedOutput}");
            }
            else
            {
                notes.Add($"ADB forward active on tcp:{hostPort} -> tcp:{devicePort}.");
            }
        }

        var report = await new BrokerBioSimulationService()
            .RunAsync(new BrokerBioSimulationOptions(
                Count: ParseInt(options, "--count", 4),
                IntervalMilliseconds: ParseInt(options, "--interval-ms", 250),
                IncludeHeartRate: !options.Has("--skip-hr"),
                IncludeEcg: !options.Has("--skip-ecg"),
                IncludeAcc: !options.Has("--skip-acc"),
                BaseBpm: ParseInt(options, "--base-bpm", 72),
                EcgSamplesPerFrame: ParseInt(options, "--ecg-samples", 8),
                AccSamplesPerFrame: ParseInt(options, "--acc-samples", 8),
                BrokerHost: host,
                BrokerPort: hostPort))
            .ConfigureAwait(false);

        var output = new
        {
            report.CapturedAt,
            report.Options,
            report.Samples,
            notes,
            report.Succeeded
        };

        if (options.TryGet("--out", out var outputRoot))
        {
            var folder = Path.Combine(outputRoot, $"broker-bio-sim-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "broker-bio-sim.json"), JsonSerializer.Serialize(output, JsonOptions));
            Console.Error.WriteLine($"Broker bio simulation bundle written to {folder}");
        }

        WriteObject(output, options.Has("--json"));
        return report.Succeeded ? 0 : 2;
    }

    private static async Task<int> BrokerShellHelperAsync(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("Use: broker shell-helper <build|start|stop|status|binary-probe> [options]");
        }

        var action = args[0].ToLowerInvariant();
        var options = ArgOptions.Parse(args.Skip(1));
        return action switch
        {
            "build" => await BrokerShellHelperBuildAsync(options).ConfigureAwait(false),
            "start" => await BrokerShellHelperRunAsync(options, disconnect: false).ConfigureAwait(false),
            "stop" => await BrokerShellHelperRunAsync(options, disconnect: true).ConfigureAwait(false),
            "status" => await BrokerShellHelperStatusAsync(options).ConfigureAwait(false),
            "binary-probe" => await BrokerShellHelperBinaryProbeAsync(options).ConfigureAwait(false),
            _ => Fail("Use: broker shell-helper <build|start|stop|status|binary-probe> [options]")
        };
    }

    private static async Task<int> BrokerShellHelperBuildAsync(ArgOptions options)
    {
        var result = await new BrokerShellHelperService()
            .BuildAsync(new BrokerShellHelperBuildOptions(
                options.ValueOrNull("--rusty-xr-root"),
                options.ValueOrNull("--android-player-root")))
            .ConfigureAwait(false);

        if (options.Has("--json"))
        {
            WriteObject(result, json: true);
        }
        else
        {
            WriteCommandResult(result);
        }
        return result.Succeeded ? 0 : result.ExitCode;
    }

    private static async Task<int> BrokerShellHelperRunAsync(ArgOptions options, bool disconnect)
    {
        var hostPort = ParsePort(options, "--host-port", BrokerClientService.DefaultPort);
        var devicePort = ParsePort(options, "--device-port", BrokerClientService.DefaultPort);
        var run = await new BrokerShellHelperService()
            .RunAsync(new BrokerShellHelperRunOptions(
                Required(options, "--serial"),
                RustyXrRoot: options.ValueOrNull("--rusty-xr-root"),
                HelperJarPath: options.ValueOrNull("--helper-jar"),
                DeviceJarPath: options.ValueOrNull("--device-jar") ?? BrokerShellHelperDefaults.DeviceJarPath,
                BrokerHost: options.ValueOrNull("--broker-host") ?? BrokerClientService.DefaultHost,
                BrokerPort: ParsePort(options, "--broker-port", BrokerClientService.DefaultPort),
                BuildBeforeRun: !options.Has("--no-build"),
                Disconnect: disconnect,
                ProbeCodecs: options.Has("--probe-codecs"),
                ProbeCameras: options.Has("--probe-cameras"),
                ProbeCameraOpen: options.Has("--probe-camera-open"),
                CameraOpenId: options.ValueOrNull("--camera-open-id") ?? string.Empty,
                EmitSyntheticVideoMetadata: options.Has("--emit-synthetic-video-metadata"),
                SyntheticVideoSamples: ParseInt(options, "--synthetic-video-samples", 0),
                EmitSyntheticVideoBinary: options.Has("--emit-synthetic-video-binary"),
                SyntheticVideoBinaryPort: ParsePort(
                    options,
                    "--binary-video-port",
                    BrokerShellHelperDefaults.SyntheticBinaryDevicePort),
                SyntheticVideoPackets: ParseInt(options, "--binary-video-packets", 0),
                SyntheticVideoPacketBytes: ParseInt(options, "--binary-video-packet-bytes", 0),
                EmitMediaCodecSyntheticVideo: options.Has("--emit-mediacodec-synthetic-video"),
                EmitScreenrecordVideo: options.Has("--emit-screenrecord-video"),
                EncodedVideoFrames: ParseInt(
                    options,
                    "--encoded-video-frames",
                    BrokerShellHelperDefaults.EncodedSyntheticDefaultFrames),
                EncodedVideoWidth: ParseInt(
                    options,
                    "--encoded-video-width",
                    BrokerShellHelperDefaults.EncodedSyntheticDefaultWidth),
                EncodedVideoHeight: ParseInt(
                    options,
                    "--encoded-video-height",
                    BrokerShellHelperDefaults.EncodedSyntheticDefaultHeight),
                EncodedVideoBitrateBps: ParseInt(
                    options,
                    "--encoded-video-bitrate",
                    BrokerShellHelperDefaults.EncodedSyntheticDefaultBitrateBps),
                ScreenrecordTimeLimitSeconds: ParseInt(
                    options,
                    "--screenrecord-time-limit",
                    BrokerShellHelperDefaults.ScreenrecordDefaultTimeLimitSeconds),
                AndroidPlayerRoot: options.ValueOrNull("--android-player-root")))
            .ConfigureAwait(false);

        CommandResult? forwardResult = null;
        JsonElement? shellHelperStatus = null;
        JsonElement? cameraProviderStatus = null;
        JsonElement? projectionProfile = null;
        string statusError = string.Empty;
        if (!options.Has("--skip-status"))
        {
            var statusProbe = await ProbeBrokerShellHelperStatusAsync(
                    options,
                    forwardSerial: Required(options, "--serial"),
                    hostPort,
                    devicePort)
                .ConfigureAwait(false);
            forwardResult = statusProbe.ForwardResult;
            shellHelperStatus = statusProbe.ShellHelperStatus;
            cameraProviderStatus = statusProbe.CameraProviderStatus;
            projectionProfile = statusProbe.ProjectionProfile;
            statusError = statusProbe.Error;
        }

        var output = new BrokerShellHelperCliReport(
            DateTimeOffset.Now,
            disconnect ? "stop" : "start",
            run,
            forwardResult,
            shellHelperStatus,
            cameraProviderStatus,
            projectionProfile,
            statusError);
        WriteObject(output, options.Has("--json"));
        return run.Succeeded && string.IsNullOrWhiteSpace(statusError) ? 0 : 2;
    }

    private static async Task<int> BrokerShellHelperStatusAsync(ArgOptions options)
    {
        var port = ParsePort(options, "--port", BrokerClientService.DefaultPort);
        var hostPort = ParsePort(options, "--host-port", port);
        var devicePort = ParsePort(options, "--device-port", BrokerClientService.DefaultPort);
        var probe = await ProbeBrokerShellHelperStatusAsync(
                options,
                options.ValueOrNull("--serial"),
                hostPort,
                devicePort)
            .ConfigureAwait(false);

        WriteObject(probe, options.Has("--json"));
        return string.IsNullOrWhiteSpace(probe.Error) ? 0 : 2;
    }

    private static async Task<int> BrokerShellHelperBinaryProbeAsync(ArgOptions options)
    {
        var useScreenrecordSource = options.Has("--screenrecord-source");
        var defaultPacketCount = useScreenrecordSource
            ? BrokerShellHelperDefaults.SyntheticBinaryMaxPacketCount
            : BrokerShellHelperDefaults.SyntheticBinaryDefaultPacketCount;
        var defaultPacketBytes = useScreenrecordSource
            ? BrokerShellHelperDefaults.ScreenrecordDefaultPacketBytes
            : BrokerShellHelperDefaults.SyntheticBinaryDefaultPacketBytes;
        var report = await new BrokerShellHelperService()
            .RunBinaryProbeAsync(new BrokerShellHelperBinaryProbeOptions(
                Required(options, "--serial"),
                RustyXrRoot: options.ValueOrNull("--rusty-xr-root"),
                HelperJarPath: options.ValueOrNull("--helper-jar"),
                DeviceJarPath: options.ValueOrNull("--device-jar") ?? BrokerShellHelperDefaults.DeviceJarPath,
                BrokerHost: options.ValueOrNull("--broker-host") ?? BrokerClientService.DefaultHost,
                BrokerPort: ParsePort(options, "--broker-port", BrokerClientService.DefaultPort),
                BuildBeforeRun: !options.Has("--no-build"),
                ProbeCodecs: options.Has("--probe-codecs"),
                ProbeCameras: options.Has("--probe-cameras"),
                ProbeCameraOpen: options.Has("--probe-camera-open"),
                CameraOpenId: options.ValueOrNull("--camera-open-id") ?? string.Empty,
                HostPort: ParsePort(
                    options,
                    "--host-port",
                    BrokerShellHelperDefaults.SyntheticBinaryHostPort),
                DevicePort: ParsePort(
                    options,
                    "--device-port",
                    BrokerShellHelperDefaults.SyntheticBinaryDevicePort),
                PacketCount: ParseInt(
                    options,
                    "--binary-video-packets",
                    defaultPacketCount),
                PacketBytes: ParseInt(
                    options,
                    "--binary-video-packet-bytes",
                    defaultPacketBytes),
                UseMediaCodecSyntheticSource: options.Has("--mediacodec-synthetic"),
                UseScreenrecordSource: useScreenrecordSource,
                EncodedVideoFrames: ParseInt(
                    options,
                    "--encoded-video-frames",
                    BrokerShellHelperDefaults.EncodedSyntheticDefaultFrames),
                EncodedVideoWidth: ParseInt(
                    options,
                    "--encoded-video-width",
                    BrokerShellHelperDefaults.EncodedSyntheticDefaultWidth),
                EncodedVideoHeight: ParseInt(
                    options,
                    "--encoded-video-height",
                    BrokerShellHelperDefaults.EncodedSyntheticDefaultHeight),
                EncodedVideoBitrateBps: ParseInt(
                    options,
                    "--encoded-video-bitrate",
                    BrokerShellHelperDefaults.EncodedSyntheticDefaultBitrateBps),
                ScreenrecordTimeLimitSeconds: ParseInt(
                    options,
                    "--screenrecord-time-limit",
                    BrokerShellHelperDefaults.ScreenrecordDefaultTimeLimitSeconds),
                ReceiverHost: options.ValueOrNull("--receiver-host") ?? BrokerClientService.DefaultHost,
                ReceiveTimeoutMilliseconds: ParseInt(options, "--timeout-ms", 20000),
                AndroidPlayerRoot: options.ValueOrNull("--android-player-root"),
                PayloadOutputPath: options.ValueOrNull("--payload-out")))
            .ConfigureAwait(false);

        WriteObject(report, options.Has("--json"));
        return report.Succeeded ? 0 : 2;
    }

    private static async Task<int> BrokerAppCameraLumaProbeAsync(ArgOptions options)
    {
        var serial = Required(options, "--serial");
        var brokerHostPort = ParsePort(options, "--broker-host-port", BrokerClientService.DefaultPort);
        var brokerDevicePort = ParsePort(options, "--broker-device-port", BrokerClientService.DefaultPort);
        var binaryHostPort = ParsePort(options, "--host-port", 18878);
        var binaryDevicePort = ParsePort(options, "--device-port", 8878);
        var frameCount = ParseInt(options, "--frame-count", 2);
        var preferredWidth = ParseInt(options, "--preferred-width", 720);
        var preferredHeight = ParseInt(options, "--preferred-height", 480);
        var timeout = TimeSpan.FromMilliseconds(ParseInt(options, "--timeout-ms", 20000));
        var adb = new QuestAdbService();

        var brokerForward = await adb.ForwardTcpAsync(serial, brokerHostPort, brokerDevicePort).ConfigureAwait(false);
        BrokerWebSocketProbeResult? command = null;
        BrokerShellHelperBinaryStreamReport? stream = null;
        string error = string.Empty;
        CommandResult? binaryForward = null;
        if (brokerForward.Succeeded)
        {
            binaryForward = await adb.ForwardTcpAsync(serial, binaryHostPort, binaryDevicePort).ConfigureAwait(false);
        }

        try
        {
            if (!brokerForward.Succeeded)
            {
                error = $"Broker ADB forward failed: {brokerForward.CondensedOutput}";
            }
            else if (binaryForward is null || !binaryForward.Succeeded)
            {
                error = $"Binary ADB forward failed: {binaryForward?.CondensedOutput ?? "not attempted"}";
            }
            else
            {
                var parameters = new JsonObject
                {
                    ["device_port"] = binaryDevicePort,
                    ["host_port"] = binaryHostPort,
                    ["frame_count"] = frameCount,
                    ["preferred_width"] = preferredWidth,
                    ["preferred_height"] = preferredHeight
                };
                var cameraId = options.ValueOrNull("--camera-id");
                if (!string.IsNullOrWhiteSpace(cameraId))
                {
                    parameters["camera_id"] = cameraId;
                }

                command = await new BrokerClientService()
                    .SendCommandAsync(
                        BrokerClientService.CreateEventsUri(
                            explicitUrl: null,
                            options.ValueOrNull("--broker-host") ?? BrokerClientService.DefaultHost,
                            brokerHostPort),
                        new BrokerCommandRequest(
                            "camera_provider.start_app_camera_luma_stream",
                            options.ValueOrNull("--request-id") ?? $"app-camera-luma-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                            options.ValueOrNull("--client-id") ?? "rusty-xr-companion-cli",
                            "Rusty XR Companion CLI",
                            AppBuildIdentity.Detect().DisplayLabel,
                            Parameters: parameters),
                        TimeSpan.Zero,
                        maxMessages: 16,
                        replyTimeout: TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);

                if (!command.HasAcceptedAck)
                {
                    error = "Broker did not accept camera_provider.start_app_camera_luma_stream.";
                }
                else
                {
                    stream = await BrokerShellHelperService.ReceiveSyntheticBinaryStreamAsync(
                            options.ValueOrNull("--receiver-host") ?? BrokerClientService.DefaultHost,
                            binaryHostPort,
                            timeout,
                            options.ValueOrNull("--payload-out"),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        var report = new BrokerAppCameraLumaProbeReport(
            DateTimeOffset.Now,
            brokerForward,
            binaryForward,
            command,
            stream,
            error);
        WriteObject(report, options.Has("--json"));
        return report.Succeeded ? 0 : 2;
    }

    private static async Task<int> BrokerAppCameraH264ProbeAsync(ArgOptions options)
    {
        var serial = Required(options, "--serial");
        var brokerHostPort = ParsePort(options, "--broker-host-port", BrokerClientService.DefaultPort);
        var brokerDevicePort = ParsePort(options, "--broker-device-port", BrokerClientService.DefaultPort);
        var binaryHostPort = ParsePort(options, "--host-port", 18879);
        var binaryDevicePort = ParsePort(options, "--device-port", 8879);
        var preferredWidth = ParseInt(options, "--preferred-width", 720);
        var preferredHeight = ParseInt(options, "--preferred-height", 480);
        var captureMs = ParseInt(options, "--capture-ms", 900);
        var maxPackets = ParseInt(options, "--max-packets", 12);
        var bitrateBps = ParseInt(options, "--bitrate-bps", 1_000_000);
        var liveStream = options.Has("--live-stream");
        var timeout = TimeSpan.FromMilliseconds(ParseInt(options, "--timeout-ms", 30000));
        var adb = new QuestAdbService();

        var brokerForward = await adb.ForwardTcpAsync(serial, brokerHostPort, brokerDevicePort).ConfigureAwait(false);
        BrokerWebSocketProbeResult? command = null;
        BrokerShellHelperBinaryStreamReport? stream = null;
        string error = string.Empty;
        CommandResult? binaryForward = null;
        if (brokerForward.Succeeded)
        {
            binaryForward = await adb.ForwardTcpAsync(serial, binaryHostPort, binaryDevicePort).ConfigureAwait(false);
        }

        try
        {
            if (!brokerForward.Succeeded)
            {
                error = $"Broker ADB forward failed: {brokerForward.CondensedOutput}";
            }
            else if (binaryForward is null || !binaryForward.Succeeded)
            {
                error = $"Binary ADB forward failed: {binaryForward?.CondensedOutput ?? "not attempted"}";
            }
            else
            {
                var parameters = new JsonObject
                {
                    ["device_port"] = binaryDevicePort,
                    ["host_port"] = binaryHostPort,
                    ["preferred_width"] = preferredWidth,
                    ["preferred_height"] = preferredHeight,
                    ["capture_ms"] = captureMs,
                    ["max_packets"] = maxPackets,
                    ["bitrate_bps"] = bitrateBps,
                    ["live_stream"] = liveStream
                };
                var cameraId = options.ValueOrNull("--camera-id");
                if (!string.IsNullOrWhiteSpace(cameraId))
                {
                    parameters["camera_id"] = cameraId;
                }

                command = await new BrokerClientService()
                    .SendCommandAsync(
                        BrokerClientService.CreateEventsUri(
                            explicitUrl: null,
                            options.ValueOrNull("--broker-host") ?? BrokerClientService.DefaultHost,
                            brokerHostPort),
                        new BrokerCommandRequest(
                            "camera_provider.start_app_camera_h264_stream",
                            options.ValueOrNull("--request-id") ?? $"app-camera-h264-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                            options.ValueOrNull("--client-id") ?? "rusty-xr-companion-cli",
                            "Rusty XR Companion CLI",
                            AppBuildIdentity.Detect().DisplayLabel,
                            Parameters: parameters),
                        TimeSpan.Zero,
                        maxMessages: 16,
                        replyTimeout: TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);

                if (!command.HasAcceptedAck)
                {
                    error = "Broker did not accept camera_provider.start_app_camera_h264_stream.";
                }
                else
                {
                    stream = await BrokerShellHelperService.ReceiveSyntheticBinaryStreamAsync(
                            options.ValueOrNull("--receiver-host") ?? BrokerClientService.DefaultHost,
                            binaryHostPort,
                            timeout,
                            options.ValueOrNull("--payload-out"),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        var report = new BrokerAppCameraH264ProbeReport(
            DateTimeOffset.Now,
            brokerForward,
            binaryForward,
            command,
            stream,
            error);
        WriteObject(report, options.Has("--json"));
        return report.Succeeded ? 0 : 2;
    }

    private static async Task<int> BrokerAppCameraH264DecodeProbeAsync(ArgOptions options)
    {
        var serial = Required(options, "--serial");
        var brokerHostPort = ParsePort(options, "--broker-host-port", BrokerClientService.DefaultPort);
        var brokerDevicePort = ParsePort(options, "--broker-device-port", BrokerClientService.DefaultPort);
        var preferredWidth = ParseInt(options, "--preferred-width", 720);
        var preferredHeight = ParseInt(options, "--preferred-height", 480);
        var captureMs = ParseInt(options, "--capture-ms", 900);
        var maxPackets = ParseInt(options, "--max-packets", 12);
        var bitrateBps = ParseInt(options, "--bitrate-bps", 1_000_000);
        var decodeTimeoutMs = ParseInt(options, "--decode-timeout-ms", 5000);
        var timeout = TimeSpan.FromMilliseconds(ParseInt(options, "--timeout-ms", 30000));
        var adb = new QuestAdbService();

        var brokerForward = await adb.ForwardTcpAsync(serial, brokerHostPort, brokerDevicePort).ConfigureAwait(false);
        BrokerWebSocketProbeResult? command = null;
        JsonElement? decodeProbe = null;
        var decodeSucceeded = false;
        string error = string.Empty;

        try
        {
            if (!brokerForward.Succeeded)
            {
                error = $"Broker ADB forward failed: {brokerForward.CondensedOutput}";
            }
            else
            {
                var parameters = new JsonObject
                {
                    ["preferred_width"] = preferredWidth,
                    ["preferred_height"] = preferredHeight,
                    ["capture_ms"] = captureMs,
                    ["max_packets"] = maxPackets,
                    ["bitrate_bps"] = bitrateBps,
                    ["decode_timeout_ms"] = decodeTimeoutMs
                };
                var cameraId = options.ValueOrNull("--camera-id");
                if (!string.IsNullOrWhiteSpace(cameraId))
                {
                    parameters["camera_id"] = cameraId;
                }

                command = await new BrokerClientService()
                    .SendCommandAsync(
                        BrokerClientService.CreateEventsUri(
                            explicitUrl: null,
                            options.ValueOrNull("--broker-host") ?? BrokerClientService.DefaultHost,
                            brokerHostPort),
                        new BrokerCommandRequest(
                            "camera_provider.run_app_camera_h264_decode_probe",
                            options.ValueOrNull("--request-id") ?? $"app-camera-h264-decode-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                            options.ValueOrNull("--client-id") ?? "rusty-xr-companion-cli",
                            "Rusty XR Companion CLI",
                            AppBuildIdentity.Detect().DisplayLabel,
                            Parameters: parameters),
                        TimeSpan.Zero,
                        maxMessages: 16,
                        replyTimeout: timeout)
                    .ConfigureAwait(false);

                decodeProbe = ExtractBrokerCommandResultChild(command, "decode_probe");
                decodeSucceeded = JsonPropertyBool(decodeProbe, "decode_succeeded");
                if (command?.HasAcceptedAck != true)
                {
                    error = "Broker did not accept camera_provider.run_app_camera_h264_decode_probe.";
                }
                else if (!decodeSucceeded)
                {
                    var lastError = JsonPropertyString(decodeProbe, "last_error");
                    error = string.IsNullOrWhiteSpace(lastError)
                        ? "Broker MediaCodec decode probe did not produce a decoded frame."
                        : lastError;
                }
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        var report = new BrokerAppCameraH264DecodeProbeReport(
            DateTimeOffset.Now,
            brokerForward,
            command,
            decodeProbe,
            decodeSucceeded,
            error);
        WriteObject(report, options.Has("--json"));
        return report.Succeeded ? 0 : 2;
    }

    private static async Task<BrokerShellHelperStatusProbe> ProbeBrokerShellHelperStatusAsync(
        ArgOptions options,
        string? forwardSerial,
        int hostPort,
        int devicePort)
    {
        CommandResult? forwardResult = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(forwardSerial))
            {
                forwardResult = await new QuestAdbService()
                    .ForwardTcpAsync(forwardSerial, hostPort, devicePort)
                    .ConfigureAwait(false);
                if (!forwardResult.Succeeded)
                {
                    return new BrokerShellHelperStatusProbe(
                        DateTimeOffset.Now,
                        forwardResult,
                        null,
                        null,
                        null,
                        $"ADB forward failed: {forwardResult.CondensedOutput}");
                }
            }

            var host = options.ValueOrNull("--host") ?? BrokerClientService.DefaultHost;
            var statusUri = BrokerClientService.CreateStatusUri(options.ValueOrNull("--status-url"), host, hostPort);
            var status = await new BrokerClientService().GetStatusAsync(statusUri).ConfigureAwait(false);
            var shellHelper = status.Status.ValueKind == JsonValueKind.Object &&
                              status.Status.TryGetProperty("shellHelper", out var shellHelperElement)
                ? shellHelperElement.Clone()
                : status.Status.Clone();
            var cameraProvider = status.Status.ValueKind == JsonValueKind.Object &&
                                 status.Status.TryGetProperty("cameraProvider", out var cameraProviderElement)
                ? cameraProviderElement.Clone()
                : (JsonElement?)null;
            var projectionProfile = status.Status.ValueKind == JsonValueKind.Object &&
                                    status.Status.TryGetProperty("projectionProfile", out var projectionProfileElement)
                ? projectionProfileElement.Clone()
                : (JsonElement?)null;
            return new BrokerShellHelperStatusProbe(
                DateTimeOffset.Now,
                forwardResult,
                shellHelper,
                cameraProvider,
                projectionProfile,
                string.Empty);
        }
        catch (Exception exception)
        {
            return new BrokerShellHelperStatusProbe(DateTimeOffset.Now, forwardResult, null, null, null, exception.Message);
        }
    }

    private static BrokerCommandRequest BrokerCommandRequest(
        string command,
        ArgOptions options,
        string requestPrefix,
        string? stream = null) =>
        new(
            command,
            $"{requestPrefix}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            options.ValueOrNull("--client-id") ?? "rusty-xr-companion-cli",
            "Rusty XR Companion CLI",
            AppBuildIdentity.Detect().DisplayLabel,
            stream);

    private static JsonElement? ExtractBrokerCommandResultChild(BrokerWebSocketProbeResult? result, string childName)
    {
        if (result is null)
        {
            return null;
        }

        foreach (var message in result.ReceivedMessages)
        {
            if (message.Payload.ValueKind != JsonValueKind.Object ||
                !message.Payload.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String ||
                !string.Equals(type.GetString(), "command_ack", StringComparison.Ordinal) ||
                !message.Payload.TryGetProperty("result", out var commandResult) ||
                commandResult.ValueKind != JsonValueKind.Object ||
                !commandResult.TryGetProperty(childName, out var child))
            {
                continue;
            }

            return child.Clone();
        }

        return null;
    }

    private static bool JsonPropertyBool(JsonElement? element, string propertyName) =>
        element.HasValue &&
        element.Value.ValueKind == JsonValueKind.Object &&
        element.Value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.True;

    private static string JsonPropertyString(JsonElement? element, string propertyName) =>
        element.HasValue &&
        element.Value.ValueKind == JsonValueKind.Object &&
        element.Value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool BrokerResultHasMessageType(BrokerWebSocketProbeResult result, string messageType) =>
        result.ReceivedMessages.Any(message =>
            string.Equals(message.Type, messageType, StringComparison.Ordinal));

    private static bool BrokerResultHasStreamEvent(BrokerWebSocketProbeResult result, string stream) =>
        result.ReceivedMessages.Any(message =>
            string.Equals(message.Type, "stream_event", StringComparison.Ordinal) &&
            message.Payload.ValueKind == JsonValueKind.Object &&
            message.Payload.TryGetProperty("stream", out var streamProperty) &&
            string.Equals(streamProperty.GetString(), stream, StringComparison.Ordinal));

    private static string BrokerStatusContractVersion(JsonElement status) =>
        status.ValueKind == JsonValueKind.Object &&
        status.TryGetProperty("contractVersion", out var version) &&
        version.ValueKind == JsonValueKind.String
            ? version.GetString() ?? string.Empty
            : string.Empty;

    private static string NormalizeBrokerCommand(string command) =>
        command.ToLowerInvariant() switch
        {
            "status" => "status_request",
            "capabilities" => "list_capabilities",
            "streams" => "list_streams",
            _ => command
        };

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

    private static int Workspace(string[] args)
    {
        var subcommand = args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal)
            ? "guide"
            : args[0].ToLowerInvariant();
        if (subcommand is not ("guide" or "status"))
        {
            return Fail("Use: workspace <guide|status> [--root <folder>] [--json]");
        }

        var options = ArgOptions.Parse(subcommand == "guide" && args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
            ? args.Skip(1)
            : subcommand == "status" && args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
                ? args.Skip(1)
                : args);
        var status = SourceWorkspaceGuide.Evaluate(options.ValueOrNull("--root"));
        if (options.Has("--json"))
        {
            WriteObject(status, json: true);
        }
        else
        {
            Console.WriteLine(SourceWorkspaceGuide.ToMarkdown(status));
        }

        var workspaceReady = status.RustyXrRepoPresent && status.CompanionRepoPresent;
        return subcommand == "status" && !workspaceReady ? 2 : 0;
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
        WriteRuntimeProfileSafetyWarning(runtimeProfile);
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
        WriteRuntimeProfileSafetyWarning(runtimeProfile);
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
            if (RuntimeProfileSafety.UsesIntentionalStrobe(runtimeProfile))
            {
                notes.Add(RuntimeProfileSafety.StrobeWarning);
            }
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
        if (RuntimeProfileLogValidator.RequiresLogValidation(runtimeProfile))
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

    private static void WriteRuntimeProfileSafetyWarning(RuntimeProfile? profile)
    {
        if (RuntimeProfileSafety.UsesIntentionalStrobe(profile))
        {
            Console.Error.WriteLine(RuntimeProfileSafety.StrobeWarning);
        }
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

    private static string WriteBrokerVerificationBundle(BrokerVerificationReport report, string outputRoot)
    {
        var folder = Path.Combine(outputRoot, $"broker-verify-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "broker-verification.json"), JsonSerializer.Serialize(report, JsonOptions));
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

    private static int ParseInt(ArgOptions options, string name, int fallback) =>
        options.TryGet(name, out var value)
            ? int.Parse(value, CultureInfo.InvariantCulture)
            : fallback;

    private static long ParseLong(ArgOptions options, string name, long fallback) =>
        options.TryGet(name, out var value)
            ? long.Parse(value, CultureInfo.InvariantCulture)
            : fallback;

    private static int ParsePort(ArgOptions options, string name, int fallback)
    {
        var port = ParseInt(options, name, fallback);
        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(name, "Port must be between 1 and 65535.");
        }

        return port;
    }

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
          status --serial <serial> [--json]
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
          media inspect-h264 --payload <file.h264> [--decode] [--ffmpeg <path>] [--timeout-ms <n>] [--json]
          media inspect-raw-luma --payload <file.raw> --width <n> --height <n> [--contact-sheet <file.pgm>] [--max-frames <n>] [--json]
          osc send [--host <host>] [--port <n>] [--address /path] [--arg kind:value] [--json]
          osc receive [--host 0.0.0.0] [--port <n>] [--count <n>] [--timeout-ms <n>] [--json]
          lsl runtime [--lsl-dll <path>] [--json]
          lsl loopback [--lsl-dll <path>] [--count <n>] [--interval-ms <n>] [--warmup-ms <n>] [--out <folder>] [--no-pdf] [--json]
          lsl broker-roundtrip [--serial <serial>] [--lsl-dll <path>] [--count <n>] [--interval-ms <n>] [--warmup-ms <n>] [--out <folder>] [--no-pdf] [--json]
          broker forward --serial <serial> [--host-port <n>] [--device-port <n>] [--json]
          broker status [--host 127.0.0.1] [--port <n>] [--url <http-url>] [--json]
          broker command --command <status|capabilities|streams|subscribe|unsubscribe|name> [--stream <id>] [--host 127.0.0.1] [--port <n>] [--url <ws-url>] [--listen-ms <n>] [--json]
          broker subscribe --stream <id> [--listen-ms <n>] [--json]
          broker sample [--subscribe] [--sequence <n>] [--path <name>] [--bytes <n>] [--listen-ms <n>] [--json]
          broker verify [--serial <serial>] [--host-port <n>] [--device-port <n>] [--osc-host <quest-ip>] [--osc-value <n>] [--out <folder>] [--json]
          broker compare --quest-host <quest-ip> [--serial <serial>] [--count <n>] [--interval-ms <n>] [--ack-port <n>] [--skip-direct-osc] [--skip-broker-osc] [--no-configure-broker-osc] [--out <folder>] [--json]
          broker bio-simulate [--serial <serial>] [--count <n>] [--interval-ms <n>] [--skip-hr] [--skip-ecg] [--skip-acc] [--base-bpm <n>] [--ecg-samples <n>] [--acc-samples <n>] [--out <folder>] [--json]
          broker app-camera-luma-probe --serial <serial> [--camera-id <id>] [--frame-count <1-6>] [--preferred-width <n>] [--preferred-height <n>] [--host-port <n>] [--device-port <n>] [--payload-out <file.raw>] [--timeout-ms <n>] [--json]
          broker app-camera-h264-probe --serial <serial> [--camera-id <id>] [--live-stream] [--capture-ms <n>] [--max-packets <1-600>] [--preferred-width <n>] [--preferred-height <n>] [--bitrate-bps <n>] [--host-port <n>] [--device-port <n>] [--payload-out <file.h264>] [--timeout-ms <n>] [--json]
          broker app-camera-h264-decode-probe --serial <serial> [--camera-id <id>] [--capture-ms <n>] [--max-packets <1-30>] [--preferred-width <n>] [--preferred-height <n>] [--bitrate-bps <n>] [--decode-timeout-ms <n>] [--json]
          broker shell-helper build [--rusty-xr-root <folder>] [--android-player-root <folder>] [--json]
          broker shell-helper start --serial <serial> [--rusty-xr-root <folder>] [--helper-jar <path>] [--no-build] [--probe-codecs] [--probe-cameras] [--probe-camera-open] [--camera-open-id <id>] [--emit-synthetic-video-metadata] [--synthetic-video-samples <0-30>] [--emit-synthetic-video-binary] [--emit-mediacodec-synthetic-video] [--emit-screenrecord-video] [--binary-video-port <n>] [--binary-video-packets <1-30>] [--binary-video-packet-bytes <1-65536>] [--encoded-video-frames <1-60>] [--encoded-video-width <n>] [--encoded-video-height <n>] [--encoded-video-bitrate <bps>] [--screenrecord-time-limit <1-3>] [--host-port <n>] [--device-port <n>] [--broker-host 127.0.0.1] [--broker-port <n>] [--skip-status] [--json]
          broker shell-helper stop --serial <serial> [--rusty-xr-root <folder>] [--helper-jar <path>] [--no-build] [--json]
          broker shell-helper status [--serial <serial>] [--host-port <n>] [--device-port <n>] [--host 127.0.0.1] [--port <n>] [--status-url <http-url>] [--json]
          broker shell-helper binary-probe --serial <serial> [--rusty-xr-root <folder>] [--helper-jar <path>] [--no-build] [--probe-cameras] [--probe-camera-open] [--camera-open-id <id>] [--mediacodec-synthetic|--screenrecord-source] [--host-port <n>] [--device-port <n>] [--binary-video-packets <1-30>] [--binary-video-packet-bytes <1-65536>] [--encoded-video-frames <1-60>] [--encoded-video-width <n>] [--encoded-video-height <n>] [--encoded-video-bitrate <bps>] [--screenrecord-time-limit <1-3>] [--payload-out <file.h264>] [--timeout-ms <n>] [--json]
          tooling status [--latest] [--json]
          tooling install-official [--json]
          workspace guide [--root <folder>] [--json]
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

    private sealed record BrokerVerificationReport(
        DateTimeOffset CapturedAt,
        Uri StatusUrl,
        Uri EventsUrl,
        CommandResult? ForwardResult,
        JsonElement Status,
        BrokerWebSocketProbeResult StreamsProbe,
        BrokerWebSocketProbeResult LatencyProbe,
        OscSendResult? OscSend,
        BrokerWebSocketProbeResult? OscProbe,
        bool StatusOk,
        bool StreamsOk,
        bool LatencyAckOk,
        bool LatencyStreamOk,
        bool OscOk,
        IReadOnlyList<string> Notes)
    {
        public bool Succeeded =>
            (ForwardResult is null || ForwardResult.Succeeded) &&
            StatusOk &&
            StreamsOk &&
            LatencyAckOk &&
            LatencyStreamOk &&
            OscOk;
    }

    private sealed record BrokerShellHelperCliReport(
        DateTimeOffset CapturedAt,
        string Action,
        BrokerShellHelperRunResult Run,
        CommandResult? ForwardResult,
        JsonElement? ShellHelperStatus,
        JsonElement? CameraProviderStatus,
        JsonElement? ProjectionProfile,
        string StatusError)
    {
        public bool Succeeded => Run.Succeeded && string.IsNullOrWhiteSpace(StatusError);
    }

    private sealed record BrokerShellHelperStatusProbe(
        DateTimeOffset CapturedAt,
        CommandResult? ForwardResult,
        JsonElement? ShellHelperStatus,
        JsonElement? CameraProviderStatus,
        JsonElement? ProjectionProfile,
        string Error);

    private sealed record BrokerAppCameraLumaProbeReport(
        DateTimeOffset CapturedAt,
        CommandResult BrokerForwardResult,
        CommandResult? BinaryForwardResult,
        BrokerWebSocketProbeResult? Command,
        BrokerShellHelperBinaryStreamReport? Stream,
        string Error)
    {
        public bool Succeeded =>
            BrokerForwardResult.Succeeded &&
            (BinaryForwardResult?.Succeeded ?? false) &&
            Command?.HasAcceptedAck == true &&
            Stream is not null &&
            string.Equals(Stream.Codec, "raw_luma8", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(Error);
    }

    private sealed record BrokerAppCameraH264ProbeReport(
        DateTimeOffset CapturedAt,
        CommandResult BrokerForwardResult,
        CommandResult? BinaryForwardResult,
        BrokerWebSocketProbeResult? Command,
        BrokerShellHelperBinaryStreamReport? Stream,
        string Error)
    {
        public bool Succeeded =>
            BrokerForwardResult.Succeeded &&
            (BinaryForwardResult?.Succeeded ?? false) &&
            Command?.HasAcceptedAck == true &&
            Stream is not null &&
            string.Equals(Stream.Codec, "h264", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(Error);
    }

    private sealed record BrokerAppCameraH264DecodeProbeReport(
        DateTimeOffset CapturedAt,
        CommandResult BrokerForwardResult,
        BrokerWebSocketProbeResult? Command,
        JsonElement? DecodeProbe,
        bool DecodeSucceeded,
        string Error)
    {
        public bool Succeeded =>
            BrokerForwardResult.Succeeded &&
            Command?.HasAcceptedAck == true &&
            DecodeSucceeded &&
            string.IsNullOrWhiteSpace(Error);
    }

    private static OscArgument ParseOscArgument(string raw)
    {
        var parts = raw.Split(':', 2);
        if (parts.Length != 2)
        {
            throw new ArgumentException($"OSC argument '{raw}' must use kind:value.");
        }

        var kind = parts[0].ToLowerInvariant();
        var value = parts[1];
        return kind switch
        {
            "int" or "i" => OscArgument.Int(int.Parse(value, CultureInfo.InvariantCulture)),
            "float" or "f" => OscArgument.Float(float.Parse(value, CultureInfo.InvariantCulture)),
            "string" or "s" => OscArgument.String(value),
            "bool" => OscArgument.Bool(bool.Parse(value)),
            "nil" => OscArgument.Nil(),
            "impulse" => OscArgument.Impulse(),
            "blob-hex" => OscArgument.Blob(Convert.FromHexString(value)),
            _ => throw new ArgumentException($"Unsupported OSC argument kind '{kind}'.")
        };
    }

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
