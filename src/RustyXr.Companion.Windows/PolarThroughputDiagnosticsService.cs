using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using RustyXr.Companion.Core;

namespace RustyXr.Companion.Windows;

public static class PolarThroughputModes
{
    public const string WindowsOwnedPmd = "windows-owned-pmd";
    public const string QuestOwnedPmd = "quest-owned-pmd";
    public const string HrRrDualReceiver = "hr-rr-dual-receiver";
}

public sealed class PolarThroughputDiagnosticsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<PolarThroughputDiagnosticReport> RunAsync(
        PolarThroughputDiagnosticOptions options,
        CancellationToken cancellationToken = default)
    {
        var normalized = options.Normalize();
        var runFolder = CreateRunFolder(normalized.OutputRoot);
        var notes = new List<string>
        {
            "Polar PMD ownership is exclusive for this diagnostic. The command modes never start Windows PMD and Quest PMD at the same time.",
            "Heart-rate/RR may be used as the dual-receiver path, but raw PMD ECG/ACC should not be treated as dual-receiver capable.",
            $"PMD stream under test: {normalized.PmdStream}; ACC request rate: {normalized.AccSampleRateHz} Hz; Windows BLE mode: {normalized.WindowsBleConnectionMode}; Quest BLE priority: {normalized.QuestBleConnectionPriority}."
        };
        var forwardSamples = new ConcurrentQueue<LslStringForwardSample>();
        var windowsRecords = new ConcurrentQueue<PolarThroughputSourceRecord>();
        CommandResult? adbForward = null;
        BrokerWebSocketProbeResult? startProbe = null;
        BrokerWebSocketProbeResult? stopProbe = null;
        BrokerWebSocketProbeResult? statusBefore = null;
        BrokerWebSocketProbeResult? statusAfter = null;
        BrokerWebSocketProbeResult? questWebSocketCapture = null;
        PolarH10WindowsCaptureResult? windowsCapture = null;
        LslStringStreamCaptureReport? questLslCapture = null;
        LslBrokerRoundTripReport? brokerRoundTrip = null;

        if (!string.IsNullOrWhiteSpace(normalized.QuestSerial))
        {
            adbForward = await new QuestAdbService()
                .ForwardTcpAsync(normalized.QuestSerial, normalized.HostPort, normalized.DevicePort)
                .ConfigureAwait(false);
            notes.Add(adbForward.Succeeded
                ? $"ADB forward active on tcp:{normalized.HostPort} -> tcp:{normalized.DevicePort}."
                : $"ADB forward failed: {adbForward.CondensedOutput}");
        }

        if (normalized.RunBrokerRoundTrip)
        {
            try
            {
                brokerRoundTrip = await new LslDiagnosticsService()
                    .RunBrokerRoundTripAsync(new LslBrokerRoundTripOptions(
                        Count: normalized.RoundTripCount,
                        IntervalMilliseconds: normalized.RoundTripIntervalMilliseconds,
                        TimeoutMilliseconds: normalized.TimeoutMilliseconds,
                        ResolveTimeoutMilliseconds: normalized.ResolveTimeoutMilliseconds,
                        WarmupMilliseconds: normalized.WarmupMilliseconds,
                        SequenceStart: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        BrokerHost: normalized.BrokerHost,
                        BrokerPort: normalized.HostPort,
                        LslDllPath: normalized.LslDllPath))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                notes.Add($"Broker LSL round-trip probe failed: {ex.Message}");
            }
        }

        switch (normalized.Mode)
        {
            case PolarThroughputModes.WindowsOwnedPmd:
                Task<LslStringStreamCaptureReport?>? questWindowsBridgeCaptureTask = null;
                if (!string.IsNullOrWhiteSpace(normalized.QuestSerial))
                {
                    questWindowsBridgeCaptureTask = CaptureWindowsBridgeOnQuestAsync(normalized, notes, cancellationToken);
                }

                try
                {
                    windowsCapture = await RunWindowsOwnedPmdAsync(
                            normalized,
                            runFolder,
                            forwardSamples,
                            windowsRecords,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    notes.Add($"Windows-owned Polar capture failed: {ex.Message}");
                }

                if (questWindowsBridgeCaptureTask is not null)
                {
                    try
                    {
                        questLslCapture = await questWindowsBridgeCaptureTask.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        notes.Add($"Quest-side Windows bridge LSL capture failed: {ex.Message}");
                    }
                }
                else
                {
                    notes.Add("Windows-owned mode publishes captured HR/RR and ACC frame records to an LSL string outlet. Pass --serial to run the Quest-side LSL inlet capture for this direction.");
                }
                break;

            case PolarThroughputModes.QuestOwnedPmd:
                (statusBefore, startProbe, questLslCapture, questWebSocketCapture, stopProbe, statusAfter) = await RunQuestOwnedAsync(
                        normalized,
                        pmd: true,
                        includeHeartRate: normalized.IncludeHeartRate,
                        requiredStream: normalized.RequiredStreamOrDefault(normalized.PmdStreamId),
                        notes,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;

            case PolarThroughputModes.HrRrDualReceiver:
                (statusBefore, startProbe, windowsCapture, questLslCapture, questWebSocketCapture, stopProbe, statusAfter) = await RunHrRrDualReceiverAsync(
                        normalized,
                        runFolder,
                        forwardSamples,
                        windowsRecords,
                        notes,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;

            default:
                throw new ArgumentException($"Unknown Polar throughput mode: {normalized.Mode}", nameof(options));
        }

        var report = new PolarThroughputDiagnosticReport(
            DateTimeOffset.UtcNow,
            runFolder,
            normalized,
            LslNativeRuntime.GetRuntimeState(normalized.LslDllPath),
            adbForward,
            brokerRoundTrip,
            statusBefore,
            startProbe,
            stopProbe,
            statusAfter,
            windowsCapture,
            questLslCapture,
            questWebSocketCapture,
            windowsRecords.ToArray(),
            forwardSamples.ToArray(),
            PolarThroughputSummary.From(windowsRecords, forwardSamples, questLslCapture, questWebSocketCapture),
            notes);
        PolarThroughputDiagnosticReportWriter.Write(report, runFolder);
        return report;
    }

    private static async Task<PolarH10WindowsCaptureResult> RunWindowsOwnedPmdAsync(
        PolarThroughputDiagnosticOptions options,
        string runFolder,
        ConcurrentQueue<LslStringForwardSample> forwardSamples,
        ConcurrentQueue<PolarThroughputSourceRecord> windowsRecords,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.EffectiveWindowsDeviceAddress))
        {
            throw new ArgumentException("--device-address or --windows-device-address is required for Windows-owned Polar capture.");
        }

        using var outlet = new LslStringOutletSession(new LslStringOutletOptions(
            options.WindowsLslStreamName,
            options.WindowsLslStreamType,
            options.WindowsLslSourceId,
            options.LslDllPath));
        var capturePath = Path.Combine(runFolder, "windows-polar-capture.jsonl");
        return await new PolarH10WindowsCaptureService()
            .CaptureAsync(
                new PolarH10WindowsCaptureOptions(
                    options.EffectiveWindowsDeviceAddress,
                    options.DurationSeconds,
                    capturePath,
                    IncludeHeartRate: options.IncludeHeartRate,
                    IncludePmdAcc: options.PmdStream == PolarH10WindowsCaptureService.PmdStreamAcc,
                    IncludePmd: true,
                    PmdStream: options.PmdStream,
                    AccSampleRateHz: options.AccSampleRateHz,
                    WindowsBleConnectionMode: options.WindowsBleConnectionMode)
                {
                    RecordObserver = (record, _) =>
                    {
                        var source = ToSourceRecord(record, "windows_ble");
                        windowsRecords.Enqueue(source);
                        var payload = BuildWindowsLslPayload(record, source, options.Mode);
                        forwardSamples.Enqueue(outlet.Push(payload, source.SourceUnixNs, source.Stream, source.Schema));
                        return ValueTask.CompletedTask;
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<LslStringStreamCaptureReport?> CaptureWindowsBridgeOnQuestAsync(
        PolarThroughputDiagnosticOptions options,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var client = new BrokerClientService();
        var eventsUri = BrokerClientService.CreateEventsUri(null, options.BrokerHost, options.HostPort);
        var parameters = new JsonObject
        {
            ["resolve_property"] = "name",
            ["resolve_value"] = options.WindowsLslStreamName,
            ["required_type"] = "polar_windows_record",
            ["required_stream"] = options.RequiredStreamOrDefault(options.PmdStreamId),
            ["duration_ms"] = options.DurationSeconds * 1000,
            ["max_samples"] = options.MaxLslSamples,
            ["resolve_timeout_ms"] = options.ResolveTimeoutMilliseconds,
            ["pull_timeout_ms"] = options.TimeoutMilliseconds,
            ["warmup_ms"] = 0
        };
        var request = new BrokerCommandRequest(
            "lsl.capture_string",
            $"polar-throughput-quest-lsl-capture-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            "rusty-xr-companion-polar-throughput",
            "Rusty XR Companion Polar Throughput",
            AppBuildIdentity.Detect().DisplayLabel,
            Parameters: parameters);
        var replyTimeout = TimeSpan.FromMilliseconds(
            options.ResolveTimeoutMilliseconds +
            options.DurationSeconds * 1000 +
            options.TimeoutMilliseconds +
            options.WarmupMilliseconds +
            5000);
        var probe = await client
            .SendCommandAsync(
                eventsUri,
                request,
                TimeSpan.Zero,
                Math.Max(4, options.MaxBrokerMessages),
                replyTimeout,
                cancellationToken)
            .ConfigureAwait(false);
        var capture = TryExtractQuestLslCapture(probe, options);
        if (capture is { Samples.Count: 0 })
        {
            notes.Add("Quest-side Windows bridge LSL capture resolved but observed no matching samples.");
        }

        return capture;
    }

    private static async Task<(
        BrokerWebSocketProbeResult? StatusBefore,
        BrokerWebSocketProbeResult? StartProbe,
        LslStringStreamCaptureReport? LslCapture,
        BrokerWebSocketProbeResult? WebSocketCapture,
        BrokerWebSocketProbeResult? StopProbe,
        BrokerWebSocketProbeResult? StatusAfter)> RunQuestOwnedAsync(
        PolarThroughputDiagnosticOptions options,
        bool pmd,
        bool includeHeartRate,
        string requiredStream,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var client = new BrokerClientService();
        var eventsUri = BrokerClientService.CreateEventsUri(null, options.BrokerHost, options.HostPort);
        var statusBefore = await SendBrokerCommandAsync(client, eventsUri, "polar.get_status", options, null, "status-before", cancellationToken)
            .ConfigureAwait(false);
        var captureTask = new LslStringDiagnosticsService()
            .CaptureAsync(new LslStringStreamCaptureOptions(
                DurationSeconds: options.DurationSeconds,
                MaxSamples: options.MaxLslSamples,
                TimeoutMilliseconds: options.TimeoutMilliseconds,
                ResolveTimeoutMilliseconds: options.ResolveTimeoutMilliseconds,
                WarmupMilliseconds: options.WarmupMilliseconds,
                ResolveProperty: "name",
                ResolveValue: options.QuestLslStreamName,
                RequiredType: "stream_event",
                RequiredStream: requiredStream,
                LslDllPath: options.LslDllPath), cancellationToken);
        var webSocketCaptureTask = CaptureBrokerStreamEventsAsync(
            client,
            eventsUri,
            requiredStream,
            options,
            cancellationToken);

        await Task.Delay(Math.Min(1000, Math.Max(0, options.WarmupMilliseconds)), cancellationToken).ConfigureAwait(false);
        JsonObject startParameters = new()
        {
            ["include_hr"] = includeHeartRate,
            ["include_pmd"] = pmd,
            ["scan_timeout_ms"] = options.ScanTimeoutMilliseconds,
            ["pmd_stream"] = options.PmdStream,
            ["acc_sample_rate_hz"] = options.AccSampleRateHz,
            ["high_connection_priority"] = options.QuestBleConnectionPriority == "high"
        };
        if (!string.IsNullOrWhiteSpace(options.EffectiveQuestDeviceAddress))
        {
            startParameters["device_address"] = options.EffectiveQuestDeviceAddress;
        }

        var startProbe = await SendBrokerCommandAsync(
                client,
                eventsUri,
                "polar.start",
                options,
                startParameters,
                "start",
                cancellationToken)
            .ConfigureAwait(false);
        LslStringStreamCaptureReport? capture = null;
        BrokerWebSocketProbeResult? webSocketCapture = null;
        BrokerWebSocketProbeResult? stopProbe = null;
        try
        {
            try
            {
                capture = await captureTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                notes.Add($"Quest LSL capture failed: {ex.Message}");
            }

            try
            {
                webSocketCapture = await webSocketCaptureTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                notes.Add($"Quest WebSocket fallback capture failed: {ex.Message}");
            }
        }
        finally
        {
            JsonObject stopParameters = new()
            {
                ["stop_hr"] = includeHeartRate,
                ["stop_pmd"] = pmd
            };
            stopProbe = await SendBrokerCommandAsync(
                    client,
                    eventsUri,
                    "polar.stop",
                    options,
                    stopParameters,
                    "stop",
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        if (capture is { Samples.Count: 0 })
        {
            notes.Add("No matching Quest-origin LSL stream events were observed. Confirm the broker APK was built with native liblsl and that LSL multicast/firewall routing is available between Quest and Windows.");
        }

        var statusAfter = await SendBrokerCommandAsync(client, eventsUri, "polar.get_status", options, null, "status-after", cancellationToken)
            .ConfigureAwait(false);
        return (statusBefore, startProbe, capture, webSocketCapture, stopProbe, statusAfter);
    }

    private static async Task<(
        BrokerWebSocketProbeResult? StatusBefore,
        BrokerWebSocketProbeResult? StartProbe,
        PolarH10WindowsCaptureResult? WindowsCapture,
        LslStringStreamCaptureReport? LslCapture,
        BrokerWebSocketProbeResult? WebSocketCapture,
        BrokerWebSocketProbeResult? StopProbe,
        BrokerWebSocketProbeResult? StatusAfter)> RunHrRrDualReceiverAsync(
        PolarThroughputDiagnosticOptions options,
        string runFolder,
        ConcurrentQueue<LslStringForwardSample> forwardSamples,
        ConcurrentQueue<PolarThroughputSourceRecord> windowsRecords,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.EffectiveWindowsDeviceAddress))
        {
            throw new ArgumentException("--device-address or --windows-device-address is required for HR/RR dual-receiver Windows capture.");
        }

        var client = new BrokerClientService();
        var eventsUri = BrokerClientService.CreateEventsUri(null, options.BrokerHost, options.HostPort);
        var statusBefore = await SendBrokerCommandAsync(client, eventsUri, "polar.get_status", options, null, "status-before", cancellationToken)
            .ConfigureAwait(false);
        var captureTask = new LslStringDiagnosticsService()
            .CaptureAsync(new LslStringStreamCaptureOptions(
                DurationSeconds: options.DurationSeconds,
                MaxSamples: options.MaxLslSamples,
                TimeoutMilliseconds: options.TimeoutMilliseconds,
                ResolveTimeoutMilliseconds: options.ResolveTimeoutMilliseconds,
                WarmupMilliseconds: options.WarmupMilliseconds,
                ResolveProperty: "name",
                ResolveValue: options.QuestLslStreamName,
                RequiredType: "stream_event",
                RequiredStream: "bio:polar_hr_rr",
                LslDllPath: options.LslDllPath), cancellationToken);
        var webSocketCaptureTask = CaptureBrokerStreamEventsAsync(
            client,
            eventsUri,
            "bio:polar_hr_rr",
            options,
            cancellationToken);
        await Task.Delay(Math.Min(1000, Math.Max(0, options.WarmupMilliseconds)), cancellationToken).ConfigureAwait(false);

        JsonObject startParameters = new()
        {
            ["scan_timeout_ms"] = options.ScanTimeoutMilliseconds
        };
        if (!string.IsNullOrWhiteSpace(options.EffectiveQuestDeviceAddress))
        {
            startParameters["device_address"] = options.EffectiveQuestDeviceAddress;
        }
        var startProbe = await SendBrokerCommandAsync(client, eventsUri, "polar_hr.start", options, startParameters, "start-hr", cancellationToken)
            .ConfigureAwait(false);

        using var outlet = new LslStringOutletSession(new LslStringOutletOptions(
            options.WindowsLslStreamName,
            options.WindowsLslStreamType,
            options.WindowsLslSourceId,
            options.LslDllPath));
        var capturePath = Path.Combine(runFolder, "windows-polar-hr-rr-capture.jsonl");
        PolarH10WindowsCaptureResult? windowsCapture = null;
        LslStringStreamCaptureReport? lslCapture = null;
        BrokerWebSocketProbeResult? webSocketCapture = null;
        BrokerWebSocketProbeResult? stopProbe = null;
        try
        {
            try
            {
                windowsCapture = await new PolarH10WindowsCaptureService()
                    .CaptureAccAsync(
                        new PolarH10WindowsCaptureOptions(
                            options.EffectiveWindowsDeviceAddress,
                            options.DurationSeconds,
                            capturePath,
                            IncludeHeartRate: true,
                            IncludePmdAcc: false)
                        {
                            RecordObserver = (record, _) =>
                            {
                                var source = ToSourceRecord(record, "windows_ble");
                                windowsRecords.Enqueue(source);
                                var payload = BuildWindowsLslPayload(record, source, options.Mode);
                                forwardSamples.Enqueue(outlet.Push(payload, source.SourceUnixNs, source.Stream, source.Schema));
                                return ValueTask.CompletedTask;
                            }
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                notes.Add($"Windows HR/RR capture failed: {ex.Message}");
            }

            try
            {
                lslCapture = await captureTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                notes.Add($"Quest HR/RR LSL capture failed: {ex.Message}");
            }

            try
            {
                webSocketCapture = await webSocketCaptureTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                notes.Add($"Quest HR/RR WebSocket fallback capture failed: {ex.Message}");
            }
        }
        finally
        {
            stopProbe = await SendBrokerCommandAsync(client, eventsUri, "polar_hr.stop", options, null, "stop-hr", CancellationToken.None)
                .ConfigureAwait(false);
        }

        if (lslCapture is { Samples.Count: 0 })
        {
            notes.Add("No Quest HR/RR LSL mirror samples were observed during the dual-receiver run.");
        }

        var statusAfter = await SendBrokerCommandAsync(client, eventsUri, "polar.get_status", options, null, "status-after", cancellationToken)
            .ConfigureAwait(false);
        return (statusBefore, startProbe, windowsCapture, lslCapture, webSocketCapture, stopProbe, statusAfter);
    }

    private static Task<BrokerWebSocketProbeResult> CaptureBrokerStreamEventsAsync(
        BrokerClientService client,
        Uri eventsUri,
        string stream,
        PolarThroughputDiagnosticOptions options,
        CancellationToken cancellationToken)
    {
        var request = new BrokerCommandRequest(
            "subscribe",
            $"polar-throughput-subscribe-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            "rusty-xr-companion-polar-throughput",
            "Rusty XR Companion Polar Throughput",
            AppBuildIdentity.Detect().DisplayLabel,
            Stream: stream);
        return client.SendMessagesAsync(
            eventsUri,
            [new BrokerWebSocketOutboundMessage("subscribe", BrokerClientService.BuildCommandPayload(request))],
            TimeSpan.FromSeconds(options.DurationSeconds),
            options.MaxLslSamples,
            TimeSpan.FromMilliseconds(options.TimeoutMilliseconds),
            cancellationToken);
    }

    private static async Task<BrokerWebSocketProbeResult> SendBrokerCommandAsync(
        BrokerClientService client,
        Uri eventsUri,
        string command,
        PolarThroughputDiagnosticOptions options,
        JsonObject? parameters,
        string requestSuffix,
        CancellationToken cancellationToken)
    {
        var request = new BrokerCommandRequest(
            command,
            $"polar-throughput-{requestSuffix}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            "rusty-xr-companion-polar-throughput",
            "Rusty XR Companion Polar Throughput",
            AppBuildIdentity.Detect().DisplayLabel,
            Parameters: parameters);
        return await client
            .SendCommandAsync(
                eventsUri,
                request,
                TimeSpan.FromMilliseconds(options.ListenMilliseconds),
                options.MaxBrokerMessages,
                TimeSpan.FromMilliseconds(options.TimeoutMilliseconds),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static PolarThroughputSourceRecord ToSourceRecord(object record, string route) =>
        record switch
        {
            PolarAccFrameRecord acc => new PolarThroughputSourceRecord(
                route,
                "bio:polar_acc",
                acc.Schema,
                acc.Sequence,
                acc.PcBleReceivedUnixNs,
                acc.SensorTimestampNs > long.MaxValue ? null : (long)acc.SensorTimestampNs,
                acc.SampleCount,
                acc.PayloadSizeBytes),
            PolarEcgFrameRecord ecg => new PolarThroughputSourceRecord(
                route,
                "bio:polar_ecg",
                ecg.Schema,
                ecg.Sequence,
                ecg.PcBleReceivedUnixNs,
                ecg.SensorTimestampNs > long.MaxValue ? null : (long)ecg.SensorTimestampNs,
                ecg.SampleCount,
                ecg.PayloadSizeBytes),
            PolarHeartRateRecord hr => new PolarThroughputSourceRecord(
                route,
                "bio:polar_hr_rr",
                hr.Schema,
                0,
                hr.TimeUnixNs,
                null,
                Math.Max(1, hr.RrIntervalsMs.Count),
                0),
            _ => new PolarThroughputSourceRecord(route, string.Empty, record.GetType().Name, 0, 0, null, 0, 0)
        };

    private static string BuildWindowsLslPayload(
        object record,
        PolarThroughputSourceRecord source,
        string route)
    {
        var payload = JsonSerializer.SerializeToNode(record, JsonOptions);
        var root = new JsonObject
        {
            ["type"] = "polar_windows_record",
            ["schema"] = "rusty.xr.companion.polar_throughput.forwarded_record.v1",
            ["route"] = route,
            ["stream"] = source.Stream,
            ["source_time_unix_ns"] = source.SourceUnixNs,
            ["source_sensor_timestamp_ns"] = source.SensorTimestampNs,
            ["source_sample_count"] = source.SampleCount,
            ["host_forward_prepare_unix_ns"] = UnixTimeNanoseconds(DateTimeOffset.UtcNow),
            ["payload"] = payload
        };
        return root.ToJsonString(JsonOptions);
    }

    private static LslStringStreamCaptureReport? TryExtractQuestLslCapture(
        BrokerWebSocketProbeResult probe,
        PolarThroughputDiagnosticOptions options)
    {
        foreach (var message in probe.ReceivedMessages)
        {
            if (message.Payload.ValueKind != JsonValueKind.Object ||
                !message.Payload.TryGetProperty("type", out var type) ||
                type.GetString() != "command_ack" ||
                !message.Payload.TryGetProperty("result", out var result) ||
                result.ValueKind != JsonValueKind.Object ||
                !result.TryGetProperty("capture", out var capture) ||
                capture.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var captureOptions = new LslStringStreamCaptureOptions(
                DurationSeconds: options.DurationSeconds,
                MaxSamples: options.MaxLslSamples,
                TimeoutMilliseconds: options.TimeoutMilliseconds,
                ResolveTimeoutMilliseconds: options.ResolveTimeoutMilliseconds,
                WarmupMilliseconds: options.WarmupMilliseconds,
                ResolveProperty: StringProperty(capture, "resolve_property", "name"),
                ResolveValue: StringProperty(capture, "resolve_value", options.WindowsLslStreamName),
                RequiredType: StringProperty(capture, "required_type", "polar_windows_record"),
                RequiredStream: StringProperty(capture, "required_stream", options.RequiredStreamOrDefault(options.PmdStreamId)),
                LslDllPath: options.LslDllPath);
            var timeCorrection = capture.TryGetProperty("time_correction", out var correctionElement) &&
                correctionElement.ValueKind == JsonValueKind.Object
                ? new LslTimeCorrectionSample(
                    DoubleProperty(correctionElement, "offset_seconds"),
                    DoubleProperty(correctionElement, "remote_time_seconds"),
                    DoubleProperty(correctionElement, "uncertainty_seconds"))
                : null;
            var samples = new List<LslStringStreamSample>();
            if (capture.TryGetProperty("samples", out var samplesElement) &&
                samplesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var sample in samplesElement.EnumerateArray())
                {
                    if (sample.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    samples.Add(new LslStringStreamSample(
                        IntProperty(sample, "index", samples.Count + 1),
                        DoubleProperty(sample, "lsl_sample_timestamp_seconds"),
                        DoubleProperty(sample, "quest_receive_lsl_clock_seconds"),
                        LongProperty(sample, "quest_receive_unix_ns") ?? 0,
                        NullableDoubleProperty(sample, "lsl_corrected_sample_to_receive_ms"),
                        NullableDoubleProperty(sample, "time_correction_offset_ms"),
                        NullableDoubleProperty(sample, "time_correction_uncertainty_ms"),
                        StringProperty(sample, "type", string.Empty),
                        StringProperty(sample, "stream", string.Empty),
                        LongProperty(sample, "sequence_id"),
                        LongProperty(sample, "broker_time_unix_ns"),
                        LongProperty(sample, "broker_time_elapsed_ns"),
                        LongProperty(sample, "source_sample_unix_ns"),
                        LongProperty(sample, "source_sample_elapsed_ns"),
                        LongProperty(sample, "broker_receive_unix_ns"),
                        LongProperty(sample, "broker_receive_elapsed_ns"),
                        LongProperty(sample, "sensor_timestamp_ns"),
                        LongProperty(sample, "sample_count"),
                        StringProperty(sample, "payload_schema", string.Empty),
                        IntProperty(sample, "payload_size_bytes", 0),
                        StringProperty(sample, "payload", string.Empty)));
                }
            }

            var notes = new List<string>();
            if (capture.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(error.GetString()))
            {
                notes.Add(error.GetString()!);
            }

            if (capture.TryGetProperty("time_correction_error", out var correctionError) &&
                correctionError.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(correctionError.GetString()))
            {
                notes.Add($"Time correction failed: {correctionError.GetString()}");
            }

            return new LslStringStreamCaptureReport(
                message.ReceivedAt,
                captureOptions,
                new LslRuntimeState(BooleanProperty(capture, "lsl_available"), "Quest broker native LSL string inlet"),
                timeCorrection,
                samples,
                LslStringStreamCaptureSummary.From(samples),
                notes);
        }

        return null;
    }

    private static string StringProperty(JsonElement element, string propertyName, string defaultValue) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? defaultValue
            : defaultValue;

    private static bool BooleanProperty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;

    private static int IntProperty(JsonElement element, string propertyName, int defaultValue) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number)
            ? number
            : defaultValue;

    private static long? LongProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static double DoubleProperty(JsonElement element, string propertyName) =>
        NullableDoubleProperty(element, propertyName) ?? 0d;

    private static double? NullableDoubleProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static string CreateRunFolder(string outputRoot)
    {
        var root = string.IsNullOrWhiteSpace(outputRoot)
            ? Path.Combine("artifacts", "polar-throughput")
            : outputRoot;
        var folder = Path.Combine(root, $"polar-throughput-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static long UnixTimeNanoseconds(DateTimeOffset value) =>
        (value.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks) * 100L;
}

public sealed record PolarThroughputDiagnosticOptions(
    string Mode = PolarThroughputModes.WindowsOwnedPmd,
    string DeviceAddress = "",
    string WindowsDeviceAddress = "",
    string QuestDeviceAddress = "",
    int DurationSeconds = 15,
    string OutputRoot = "",
    bool IncludeHeartRate = true,
    string PmdStream = PolarH10WindowsCaptureService.PmdStreamAcc,
    int AccSampleRateHz = 200,
    string WindowsBleConnectionMode = PolarH10WindowsCaptureService.WindowsBleConnectionDefault,
    string QuestBleConnectionPriority = "high",
    string QuestSerial = "",
    string BrokerHost = BrokerClientService.DefaultHost,
    int HostPort = BrokerClientService.DefaultPort,
    int DevicePort = BrokerClientService.DefaultPort,
    int ScanTimeoutMilliseconds = 30000,
    int TimeoutMilliseconds = 5000,
    int ResolveTimeoutMilliseconds = 10000,
    int WarmupMilliseconds = 500,
    int ListenMilliseconds = 0,
    int MaxBrokerMessages = 24,
    int MaxLslSamples = 2048,
    string RequiredStream = "",
    string LslDllPath = "",
    string QuestLslStreamName = LslDiagnosticDefaults.BrokerLatencyStreamName,
    string WindowsLslStreamName = "rusty_xr_polar_windows_bridge",
    string WindowsLslStreamType = "rusty.xr.polar.diagnostic",
    string WindowsLslSourceId = "",
    bool RunBrokerRoundTrip = true,
    int RoundTripCount = 8,
    int RoundTripIntervalMilliseconds = 250)
{
    public PolarThroughputDiagnosticOptions Normalize() =>
        this with
        {
            Mode = string.IsNullOrWhiteSpace(Mode) ? PolarThroughputModes.WindowsOwnedPmd : Mode.Trim().ToLowerInvariant(),
            DeviceAddress = DeviceAddress?.Trim() ?? string.Empty,
            WindowsDeviceAddress = WindowsDeviceAddress?.Trim() ?? string.Empty,
            QuestDeviceAddress = QuestDeviceAddress?.Trim() ?? string.Empty,
            DurationSeconds = Math.Clamp(DurationSeconds, 3, 300),
            OutputRoot = OutputRoot?.Trim() ?? string.Empty,
            PmdStream = NormalizePmdStream(PmdStream),
            AccSampleRateHz = NormalizeAccSampleRate(AccSampleRateHz),
            WindowsBleConnectionMode = NormalizeWindowsBleConnectionMode(WindowsBleConnectionMode),
            QuestBleConnectionPriority = NormalizeQuestBleConnectionPriority(QuestBleConnectionPriority),
            QuestSerial = QuestSerial?.Trim() ?? string.Empty,
            BrokerHost = string.IsNullOrWhiteSpace(BrokerHost) ? BrokerClientService.DefaultHost : BrokerHost.Trim(),
            HostPort = HostPort is > 0 and <= 65535 ? HostPort : BrokerClientService.DefaultPort,
            DevicePort = DevicePort is > 0 and <= 65535 ? DevicePort : BrokerClientService.DefaultPort,
            ScanTimeoutMilliseconds = Math.Clamp(ScanTimeoutMilliseconds, 1000, 120_000),
            TimeoutMilliseconds = Math.Clamp(TimeoutMilliseconds, 100, 60_000),
            ResolveTimeoutMilliseconds = Math.Clamp(ResolveTimeoutMilliseconds, 100, 60_000),
            WarmupMilliseconds = Math.Clamp(WarmupMilliseconds, 0, 60_000),
            ListenMilliseconds = Math.Clamp(ListenMilliseconds, 0, 60_000),
            MaxBrokerMessages = Math.Clamp(MaxBrokerMessages, 1, 1000),
            MaxLslSamples = Math.Clamp(MaxLslSamples, 1, 100_000),
            RequiredStream = RequiredStream?.Trim() ?? string.Empty,
            LslDllPath = LslDllPath?.Trim() ?? string.Empty,
            QuestLslStreamName = string.IsNullOrWhiteSpace(QuestLslStreamName) ? LslDiagnosticDefaults.BrokerLatencyStreamName : QuestLslStreamName.Trim(),
            WindowsLslStreamName = string.IsNullOrWhiteSpace(WindowsLslStreamName) ? "rusty_xr_polar_windows_bridge" : WindowsLslStreamName.Trim(),
            WindowsLslStreamType = string.IsNullOrWhiteSpace(WindowsLslStreamType) ? "rusty.xr.polar.diagnostic" : WindowsLslStreamType.Trim(),
            WindowsLslSourceId = WindowsLslSourceId?.Trim() ?? string.Empty,
            RoundTripCount = Math.Clamp(RoundTripCount, 1, 1000),
            RoundTripIntervalMilliseconds = Math.Clamp(RoundTripIntervalMilliseconds, 0, 60_000)
        };

    public string RequiredStreamOrDefault(string value) =>
        string.IsNullOrWhiteSpace(RequiredStream) ? value : RequiredStream;

    public string PmdStreamId =>
        PmdStream == PolarH10WindowsCaptureService.PmdStreamEcg ? "bio:polar_ecg" : "bio:polar_acc";

    public string EffectiveWindowsDeviceAddress =>
        string.IsNullOrWhiteSpace(WindowsDeviceAddress) ? DeviceAddress : WindowsDeviceAddress;

    public string EffectiveQuestDeviceAddress =>
        string.IsNullOrWhiteSpace(QuestDeviceAddress) ? DeviceAddress : QuestDeviceAddress;

    private static string NormalizePmdStream(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "ecg" or "bio:polar_ecg" or "polar_ecg"
            ? PolarH10WindowsCaptureService.PmdStreamEcg
            : PolarH10WindowsCaptureService.PmdStreamAcc;
    }

    private static int NormalizeAccSampleRate(int value) =>
        value is 25 or 50 or 100 or 200 ? value : 200;

    private static string NormalizeWindowsBleConnectionMode(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Replace('_', '-');
        return normalized is "throughput" or "throughput-optimized" or "high-throughput"
            ? PolarH10WindowsCaptureService.WindowsBleConnectionThroughputOptimized
            : PolarH10WindowsCaptureService.WindowsBleConnectionDefault;
    }

    private static string NormalizeQuestBleConnectionPriority(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "balanced" or "default" or "normal" ? "default" : "high";
    }
}

public sealed record PolarThroughputDiagnosticReport(
    DateTimeOffset CapturedAt,
    string ArtifactFolder,
    PolarThroughputDiagnosticOptions Options,
    LslRuntimeState LslRuntime,
    CommandResult? AdbForward,
    LslBrokerRoundTripReport? BrokerRoundTrip,
    BrokerWebSocketProbeResult? BrokerStatusBefore,
    BrokerWebSocketProbeResult? BrokerStart,
    BrokerWebSocketProbeResult? BrokerStop,
    BrokerWebSocketProbeResult? BrokerStatusAfter,
    PolarH10WindowsCaptureResult? WindowsCapture,
    LslStringStreamCaptureReport? QuestLslCapture,
    BrokerWebSocketProbeResult? QuestWebSocketCapture,
    IReadOnlyList<PolarThroughputSourceRecord> WindowsRecords,
    IReadOnlyList<LslStringForwardSample> WindowsLslForwardSamples,
    PolarThroughputSummary Summary,
    IReadOnlyList<string> Notes);

public sealed record PolarThroughputSourceRecord(
    string Route,
    string Stream,
    string Schema,
    long Sequence,
    long SourceUnixNs,
    long? SensorTimestampNs,
    long SampleCount,
    int PayloadSizeBytes);

public sealed record PolarThroughputSummary(
    TimingCadenceSummary WindowsSourceCadence,
    TimingCadenceSummary WindowsLslPushCadence,
    TimingCadenceSummary QuestLslReceiveCadence,
    TimingCadenceSummary QuestWebSocketReceiveCadence,
    long WindowsSensorSamples,
    double? WindowsSensorSampleRateHz,
    long QuestSensorSamples,
    double? QuestSensorSampleRateHz,
    double? MeanWindowsSourceToLslPushMs,
    double? MeanQuestLslReceiveDelayMs,
    double? MeanLslTimeCorrectionUncertaintyMs,
    IReadOnlyList<PolarStreamThroughputSummary> Streams)
{
    public static PolarThroughputSummary From(
        IEnumerable<PolarThroughputSourceRecord> windowsRecords,
        IEnumerable<LslStringForwardSample> forwardSamples,
        LslStringStreamCaptureReport? questLslCapture,
        BrokerWebSocketProbeResult? questWebSocketCapture)
    {
        var records = windowsRecords.ToArray();
        var forwards = forwardSamples.ToArray();
        var sourceCadence = TimingCadenceSummary.FromUnixNs(records.Select(static record => (long?)record.SourceUnixNs));
        var windowsSensorSamples = records.Sum(static record => record.SampleCount);
        double? windowsSensorRate = sourceCadence.DurationMs is > 0
            ? windowsSensorSamples / (sourceCadence.DurationMs.Value / 1000d)
            : null;
        return new PolarThroughputSummary(
            sourceCadence,
            TimingCadenceSummary.FromUnixNs(forwards.Where(static sample => sample.Pushed).Select(static sample => (long?)sample.HostPushUnixNs)),
            questLslCapture?.Summary.HostReceiveCadence ?? TimingCadenceSummary.Empty,
            TimingCadenceSummary.FromUnixNs(QuestStreamMessages(questWebSocketCapture).Select(static message => (long?)UnixTimeNanoseconds(message.ReceivedAt))),
            windowsSensorSamples,
            windowsSensorRate,
            questLslCapture?.Summary.SensorSampleCount ?? 0,
            questLslCapture?.Summary.SensorSampleRateHz,
            Mean(forwards.Select(static sample => sample.SourceToPushMs)),
            questLslCapture?.Summary.MeanLslReceiveDelayMs,
            questLslCapture?.Summary.MeanTimeCorrectionUncertaintyMs,
            BuildStreamSummaries(records, forwards, questLslCapture, questWebSocketCapture));
    }

    private static IReadOnlyList<PolarStreamThroughputSummary> BuildStreamSummaries(
        IReadOnlyList<PolarThroughputSourceRecord> records,
        IReadOnlyList<LslStringForwardSample> forwards,
        LslStringStreamCaptureReport? questLslCapture,
        BrokerWebSocketProbeResult? questWebSocketCapture)
    {
        var questSamples = questLslCapture?.Samples ?? [];
        var websocketMessages = QuestStreamMessages(questWebSocketCapture).ToArray();
        var streams = records.Select(static record => record.Stream)
            .Concat(forwards.Select(static sample => sample.Stream))
            .Concat(questSamples.Select(static sample => sample.Stream))
            .Concat(websocketMessages.Select(StreamFromMessage))
            .Where(static stream => !string.IsNullOrWhiteSpace(stream))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var summaries = new List<PolarStreamThroughputSummary>(streams.Length);
        foreach (var stream in streams)
        {
            var streamRecords = records.Where(record => string.Equals(record.Stream, stream, StringComparison.Ordinal)).ToArray();
            var streamForwards = forwards.Where(sample => string.Equals(sample.Stream, stream, StringComparison.Ordinal)).ToArray();
            var streamQuestSamples = questSamples.Where(sample => string.Equals(sample.Stream, stream, StringComparison.Ordinal)).ToArray();
            var streamWebsocketMessages = websocketMessages.Where(message => string.Equals(StreamFromMessage(message), stream, StringComparison.Ordinal)).ToArray();
            var sourceCadence = TimingCadenceSummary.FromUnixNs(streamRecords.Select(static record => (long?)record.SourceUnixNs));
            var sensorSamples = streamRecords.Sum(static record => record.SampleCount);
            double? sensorRate = sourceCadence.DurationMs is > 0
                ? sensorSamples / (sourceCadence.DurationMs.Value / 1000d)
                : null;
            var questSummary = LslStringStreamCaptureSummary.From(streamQuestSamples);
            summaries.Add(new PolarStreamThroughputSummary(
                stream,
                streamRecords.Length,
                sourceCadence,
                TimingCadenceSummary.FromUnixNs(streamForwards.Where(static sample => sample.Pushed).Select(static sample => (long?)sample.HostPushUnixNs)),
                questSummary.HostReceiveCadence,
                TimingCadenceSummary.FromUnixNs(streamWebsocketMessages.Select(static message => (long?)UnixTimeNanoseconds(message.ReceivedAt))),
                sensorSamples,
                sensorRate,
                questSummary.SensorSampleCount,
                questSummary.SensorSampleRateHz,
                Mean(streamForwards.Select(static sample => sample.SourceToPushMs)),
                questSummary.MeanLslReceiveDelayMs,
                questSummary.MeanTimeCorrectionUncertaintyMs,
                PayloadSizeSummary.From(streamRecords.Select(static record => record.PayloadSizeBytes)),
                PayloadSizeSummary.From(streamQuestSamples.Select(static sample => sample.PayloadSizeBytes))));
        }

        return summaries;
    }

    private static IEnumerable<BrokerWebSocketReceivedMessage> QuestStreamMessages(BrokerWebSocketProbeResult? probe) =>
        probe?.ReceivedMessages.Where(static message =>
            message.Payload.ValueKind == JsonValueKind.Object &&
            message.Payload.TryGetProperty("type", out var type) &&
            type.ValueKind == JsonValueKind.String &&
            string.Equals(type.GetString(), "stream_event", StringComparison.Ordinal)) ?? [];

    private static string StreamFromMessage(BrokerWebSocketReceivedMessage message) =>
        message.Payload.TryGetProperty("stream", out var stream) && stream.ValueKind == JsonValueKind.String
            ? stream.GetString() ?? string.Empty
            : string.Empty;

    private static long UnixTimeNanoseconds(DateTimeOffset value) =>
        (value.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks) * 100L;

    private static double? Mean(IEnumerable<double?> values)
    {
        var materialized = values.Where(static value => value.HasValue).Select(static value => value!.Value).ToArray();
        return materialized.Length == 0 ? null : materialized.Average();
    }
}

public sealed record PolarStreamThroughputSummary(
    string Stream,
    long WindowsRecordCount,
    TimingCadenceSummary WindowsSourceCadence,
    TimingCadenceSummary WindowsLslPushCadence,
    TimingCadenceSummary QuestLslReceiveCadence,
    TimingCadenceSummary QuestWebSocketReceiveCadence,
    long WindowsSensorSamples,
    double? WindowsSensorSampleRateHz,
    long QuestSensorSamples,
    double? QuestSensorSampleRateHz,
    double? MeanWindowsSourceToLslPushMs,
    double? MeanQuestLslReceiveDelayMs,
    double? MeanLslTimeCorrectionUncertaintyMs,
    PayloadSizeSummary WindowsNotificationPayloadBytes,
    PayloadSizeSummary QuestObservedPayloadBytes);

public sealed record PayloadSizeSummary(
    long Count,
    int MinBytes,
    int MaxBytes,
    double? MeanBytes,
    IReadOnlyDictionary<int, long> Histogram)
{
    public static PayloadSizeSummary From(IEnumerable<int> payloadSizes)
    {
        var values = payloadSizes.Where(static value => value > 0).ToArray();
        if (values.Length == 0)
        {
            return new PayloadSizeSummary(0, 0, 0, null, new SortedDictionary<int, long>());
        }

        var histogram = new SortedDictionary<int, long>();
        foreach (var value in values)
        {
            histogram[value] = histogram.TryGetValue(value, out var count) ? count + 1 : 1;
        }

        return new PayloadSizeSummary(values.Length, values.Min(), values.Max(), values.Average(), histogram);
    }
}

public static class PolarThroughputDiagnosticReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static void Write(PolarThroughputDiagnosticReport report, string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "polar-throughput-report.json"), JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(Path.Combine(folder, "polar-throughput-summary.md"), ToMarkdown(report));
        File.WriteAllText(Path.Combine(folder, "windows-lsl-forward.csv"), ForwardCsv(report.WindowsLslForwardSamples));
        if (report.QuestLslCapture is not null)
        {
            File.WriteAllText(Path.Combine(folder, "quest-lsl-observed.csv"), QuestLslCsv(report.QuestLslCapture.Samples));
        }
    }

    private static string ToMarkdown(PolarThroughputDiagnosticReport report) =>
        $"""
        # Polar Throughput Diagnostic

        - captured: {report.CapturedAt:O}
        - mode: {report.Options.Mode}
        - artifact folder: {report.ArtifactFolder}
        - LSL runtime: {(report.LslRuntime.Available ? "available" : "unavailable")} - {report.LslRuntime.Detail}
        - Windows records: {report.WindowsRecords.Count}
        - Windows source cadence: {Format(report.Summary.WindowsSourceCadence.RateHz)} Hz, mean interval {Format(report.Summary.WindowsSourceCadence.MeanIntervalMs)} ms
        - Windows LSL push cadence: {Format(report.Summary.WindowsLslPushCadence.RateHz)} Hz, mean source-to-push {Format(report.Summary.MeanWindowsSourceToLslPushMs)} ms
        - Quest LSL observed samples: {report.QuestLslCapture?.Samples.Count ?? 0}
        - Quest LSL receive cadence: {Format(report.Summary.QuestLslReceiveCadence.RateHz)} Hz, mean receive delay {Format(report.Summary.MeanQuestLslReceiveDelayMs)} ms
        - Windows sensor samples: {report.Summary.WindowsSensorSamples} at {Format(report.Summary.WindowsSensorSampleRateHz)} Hz
        - Quest sensor samples: {report.Summary.QuestSensorSamples} at {Format(report.Summary.QuestSensorSampleRateHz)} Hz
        - LSL clock uncertainty: {Format(report.Summary.MeanLslTimeCorrectionUncertaintyMs)} ms

        ## Stream Summaries

        {StreamSummariesMarkdown(report.Summary.Streams)}

        ## Notes

        {string.Join(Environment.NewLine, report.Notes.Select(static note => $"- {note}"))}
        """;

    private static string StreamSummariesMarkdown(IReadOnlyList<PolarStreamThroughputSummary> streams)
    {
        if (streams.Count == 0)
        {
            return "- No stream-specific samples were observed.";
        }

        return string.Join(
            Environment.NewLine,
            streams.Select(stream =>
                $"- {stream.Stream}: windows records {stream.WindowsRecordCount}, windows source {Format(stream.WindowsSourceCadence.RateHz)} Hz, windows sensor {stream.WindowsSensorSamples} at {Format(stream.WindowsSensorSampleRateHz)} Hz, Quest LSL samples {stream.QuestLslReceiveCadence.Count}, Quest sensor {stream.QuestSensorSamples} at {Format(stream.QuestSensorSampleRateHz)} Hz, Windows payload bytes {PayloadSummary(stream.WindowsNotificationPayloadBytes)}, Quest payload bytes {PayloadSummary(stream.QuestObservedPayloadBytes)}"));
    }

    private static string PayloadSummary(PayloadSizeSummary summary) =>
        summary.Count == 0
            ? "n/a"
            : $"n={summary.Count} min={summary.MinBytes} mean={Format(summary.MeanBytes)} max={summary.MaxBytes}";

    private static string ForwardCsv(IReadOnlyList<LslStringForwardSample> samples)
    {
        var lines = new List<string>
        {
            "sequence,pushed,stream,schema,source_unix_ns,host_push_unix_ns,source_to_push_ms,payload_size_bytes,error"
        };
        lines.AddRange(samples.Select(static sample => string.Join(
            ",",
            sample.Sequence.ToString(CultureInfo.InvariantCulture),
            sample.Pushed ? "true" : "false",
            Escape(sample.Stream),
            Escape(sample.Schema),
            sample.SourceUnixNs.ToString(CultureInfo.InvariantCulture),
            sample.HostPushUnixNs.ToString(CultureInfo.InvariantCulture),
            Format(sample.SourceToPushMs),
            sample.PayloadSizeBytes.ToString(CultureInfo.InvariantCulture),
            Escape(sample.Error))));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string QuestLslCsv(IReadOnlyList<LslStringStreamSample> samples)
    {
        var lines = new List<string>
        {
            "index,stream,sequence_id,host_receive_unix_ns,broker_time_unix_ns,source_sample_unix_ns,broker_receive_unix_ns,sensor_sample_count,lsl_receive_delay_ms,time_correction_uncertainty_ms,payload_size_bytes"
        };
        lines.AddRange(samples.Select(static sample => string.Join(
            ",",
            sample.Index.ToString(CultureInfo.InvariantCulture),
            Escape(sample.Stream),
            sample.SequenceId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            sample.HostReceiveUnixNs.ToString(CultureInfo.InvariantCulture),
            sample.BrokerTimeUnixNs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            sample.SourceSampleUnixNs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            sample.BrokerReceiveUnixNs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            sample.SensorSampleCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Format(sample.LslCorrectedSampleToReceiveMs),
            Format(sample.TimeCorrectionUncertaintyMs),
            sample.PayloadSizeBytes.ToString(CultureInfo.InvariantCulture))));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static string Format(double? value) =>
        value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
}
