using System.Globalization;
using System.Text.Json;

namespace RustyXr.Companion.Core;

public static class LslDiagnosticDefaults
{
    public const string LocalLoopbackStreamType = "rusty.xr.diagnostics.roundtrip";
    public const string BrokerLatencyStreamName = "rusty_xr_broker_latency";
    public const string BrokerLatencyStreamType = "rusty.xr.latency";
}

public sealed class LslDiagnosticsService
{
    public async Task<LslLocalRoundTripReport> RunLocalLoopbackAsync(
        LslLocalRoundTripOptions options,
        CancellationToken cancellationToken = default)
    {
        var normalized = options.Normalize();
        var runtime = LslNativeRuntime.GetRuntimeState(normalized.LslDllPath);
        if (!runtime.Available)
        {
            return new LslLocalRoundTripReport(
                DateTimeOffset.UtcNow,
                normalized,
                runtime,
                null,
                [],
                LslRoundTripSummary.Empty,
                ["LSL runtime unavailable."]);
        }

        var sourceId = string.IsNullOrWhiteSpace(normalized.SourceId)
            ? $"rusty-xr-companion-local-{Guid.NewGuid():N}"
            : normalized.SourceId.Trim();
        var streamName = string.IsNullOrWhiteSpace(normalized.StreamName)
            ? $"rusty_xr_lsl_loopback_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}"
            : normalized.StreamName.Trim();
        var samples = new List<LslLocalRoundTripSample>(normalized.Count);
        var notes = new List<string>();

        using var outlet = LslNativeRuntime.CreateDoubleOutlet(
            streamName,
            normalized.StreamType,
            sourceId,
            4);
        using var inlet = LslNativeRuntime.ResolveDoubleInlet(
            "source_id",
            sourceId,
            TimeSpan.FromMilliseconds(normalized.ResolveTimeoutMilliseconds),
            4);
        inlet.Open(TimeSpan.FromMilliseconds(normalized.TimeoutMilliseconds));
        var correction = LslNativeRuntime.GetTimeCorrection(
            inlet.Handle,
            TimeSpan.FromMilliseconds(normalized.TimeoutMilliseconds));
        if (normalized.WarmupMilliseconds > 0)
        {
            await Task.Delay(normalized.WarmupMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        PrimeLocalLoopback(outlet, inlet, normalized, notes);

        for (var index = 0; index < normalized.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sequence = index + 1;
            var sendUnixNs = UnixTimeNanoseconds(DateTimeOffset.UtcNow);
            var sendClock = LslNativeRuntime.LocalClock();
            var value01 = normalized.Count == 1 ? 1d : (double)index / (normalized.Count - 1);
            outlet.Push([sequence, sendUnixNs / 1_000_000d, sendClock, value01], sendClock);

            var pulled = inlet.Pull(TimeSpan.FromMilliseconds(normalized.TimeoutMilliseconds));
            var receiveClock = LslNativeRuntime.LocalClock();
            var receiveUnixNs = UnixTimeNanoseconds(DateTimeOffset.UtcNow);
            if (pulled is null)
            {
                samples.Add(new LslLocalRoundTripSample(
                    sequence,
                    sendUnixNs,
                    sendClock,
                    value01,
                    false,
                    null,
                    receiveClock,
                    receiveUnixNs,
                    null,
                    null,
                    null,
                    null));
            }
            else
            {
                var values = pulled.Values;
                var sampleSequence = values.Length > 0 ? (int)Math.Round(values[0]) : sequence;
                var correctedSampleClock = pulled.TimestampSeconds + correction.OffsetSeconds;
                samples.Add(new LslLocalRoundTripSample(
                    sequence,
                    sendUnixNs,
                    sendClock,
                    value01,
                    sampleSequence == sequence,
                    pulled.TimestampSeconds,
                    receiveClock,
                    receiveUnixNs,
                    (receiveClock - correctedSampleClock) * 1000d,
                    (receiveUnixNs - sendUnixNs) / 1_000_000d,
                    correction.OffsetSeconds * 1000d,
                    correction.UncertaintySeconds * 1000d));
            }

            if (index + 1 < normalized.Count)
            {
                await Task.Delay(normalized.IntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        return new LslLocalRoundTripReport(
            DateTimeOffset.UtcNow,
            normalized with { StreamName = streamName, SourceId = sourceId },
            runtime,
            correction,
            samples,
            LslRoundTripSummary.FromLocal(samples),
            notes);
    }

    public async Task<LslBrokerRoundTripReport> RunBrokerRoundTripAsync(
        LslBrokerRoundTripOptions options,
        CancellationToken cancellationToken = default)
    {
        var normalized = options.Normalize();
        var runtime = LslNativeRuntime.GetRuntimeState(normalized.LslDllPath);
        if (!runtime.Available)
        {
            return new LslBrokerRoundTripReport(
                DateTimeOffset.UtcNow,
                normalized,
                runtime,
                null,
                [],
                LslRoundTripSummary.Empty,
                ["LSL runtime unavailable."]);
        }

        var eventsUri = BrokerClientService.CreateEventsUri(null, normalized.BrokerHost, normalized.BrokerPort);
        var brokerClient = new BrokerClientService();
        var samples = new List<LslBrokerRoundTripSample>(normalized.Count);
        var notes = new List<string>();
        var appVersion = AppBuildIdentity.Detect().DisplayLabel;

        using var inlet = LslNativeRuntime.ResolveStringInlet(
            "name",
            normalized.StreamName,
            TimeSpan.FromMilliseconds(normalized.ResolveTimeoutMilliseconds));
        inlet.Open(TimeSpan.FromMilliseconds(normalized.TimeoutMilliseconds));
        var correction = LslNativeRuntime.GetTimeCorrection(
            inlet.Handle,
            TimeSpan.FromMilliseconds(normalized.TimeoutMilliseconds));
        if (normalized.WarmupMilliseconds > 0)
        {
            await Task.Delay(normalized.WarmupMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        for (var index = 0; index < normalized.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sequence = normalized.SequenceStart + index;
            var sendAt = DateTimeOffset.UtcNow;
            var request = new BrokerLatencySampleRequest(
                sequence,
                normalized.Path,
                normalized.PayloadSizeBytes,
                "rusty-xr-companion-cli",
                "Rusty XR Companion CLI",
                appVersion);

            var payload = BrokerClientService.BuildLatencySamplePayload(request, sendAt);
            var clientSendUnixNs = TryGetInt64(payload, "client_send_time_unix_ns") ?? UnixTimeNanoseconds(sendAt);
            var probe = await brokerClient
                .SendMessagesAsync(
                    eventsUri,
                    [new BrokerWebSocketOutboundMessage("latency_sample", payload)],
                    TimeSpan.Zero,
                    maxMessages: 8,
                    replyTimeout: TimeSpan.FromMilliseconds(normalized.TimeoutMilliseconds),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var ack = FindLatencyAck(probe, sequence);

            var deadline = DateTimeOffset.UtcNow.AddMilliseconds(normalized.TimeoutMilliseconds);
            LslStringSample? pulled = null;
            JsonDocument? parsedDocument = null;
            JsonElement parsed = default;
            while (DateTimeOffset.UtcNow < deadline)
            {
                pulled = inlet.Pull(TimeSpan.FromMilliseconds(Math.Min(250, normalized.TimeoutMilliseconds)));
                if (pulled is null || pulled.Values.Length == 0 || string.IsNullOrWhiteSpace(pulled.Values[0]))
                {
                    continue;
                }

                try
                {
                    parsedDocument?.Dispose();
                    parsedDocument = JsonDocument.Parse(pulled.Values[0]);
                    parsed = parsedDocument.RootElement.Clone();
                }
                catch (JsonException)
                {
                    continue;
                }

                if (TryGetInt64(parsed, "sequence_id") == sequence)
                {
                    break;
                }

                pulled = null;
            }

            var receiveClock = LslNativeRuntime.LocalClock();
            var receiveUnixNs = UnixTimeNanoseconds(DateTimeOffset.UtcNow);
            var brokerReceiveUnixNs = pulled is null ? null : TryGetInt64(parsed, "broker_receive_time_unix_ns");
            var brokerPublishUnixNs = pulled is null ? null : TryGetInt64(parsed, "broker_publish_time_unix_ns");
            double? correctedSampleClock = pulled is null ? null : pulled.TimestampSeconds + correction.OffsetSeconds;

            samples.Add(new LslBrokerRoundTripSample(
                sequence,
                clientSendUnixNs,
                ack?.ReceivedAt,
                pulled is not null,
                pulled?.TimestampSeconds,
                receiveClock,
                receiveUnixNs,
                brokerReceiveUnixNs,
                brokerPublishUnixNs,
                pulled is null || correctedSampleClock is null ? null : (receiveClock - correctedSampleClock.Value) * 1000d,
                (receiveUnixNs - clientSendUnixNs) / 1_000_000d,
                brokerReceiveUnixNs is null ? null : (brokerReceiveUnixNs.Value - clientSendUnixNs) / 1_000_000d,
                brokerReceiveUnixNs is null || brokerPublishUnixNs is null ? null : (brokerPublishUnixNs.Value - brokerReceiveUnixNs.Value) / 1_000_000d,
                correction.OffsetSeconds * 1000d,
                correction.UncertaintySeconds * 1000d,
                ack?.Payload,
                pulled?.Values.FirstOrDefault(),
                probe.ReceivedMessages.Count));

            parsedDocument?.Dispose();

            if (index + 1 < normalized.Count)
            {
                await Task.Delay(normalized.IntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        return new LslBrokerRoundTripReport(
            DateTimeOffset.UtcNow,
            normalized,
            runtime,
            correction,
            samples,
            LslRoundTripSummary.FromBroker(samples),
            notes);
    }

    private static BrokerWebSocketReceivedMessage? FindLatencyAck(BrokerWebSocketProbeResult probe, long sequence) =>
        probe.ReceivedMessages.FirstOrDefault(message =>
            message.Payload.ValueKind == JsonValueKind.Object &&
            message.Payload.TryGetProperty("type", out var type) &&
            type.ValueKind == JsonValueKind.String &&
            string.Equals(type.GetString(), "latency_ack", StringComparison.Ordinal) &&
            TryGetInt64(message.Payload, "sequence_id") == sequence);

    private static void PrimeLocalLoopback(
        LslNativeRuntime.LslDoubleOutlet outlet,
        LslNativeRuntime.LslDoubleInlet inlet,
        LslLocalRoundTripOptions options,
        List<string> notes)
    {
        var primeClock = LslNativeRuntime.LocalClock();
        outlet.Push([0d, 0d, primeClock, 0d], primeClock);

        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(Math.Min(options.TimeoutMilliseconds, 1000));
        while (DateTimeOffset.UtcNow < deadline)
        {
            var pulled = inlet.Pull(TimeSpan.FromMilliseconds(100));
            if (pulled is null || pulled.Values.Length == 0)
            {
                continue;
            }

            var sequence = (int)Math.Round(pulled.Values[0]);
            if (sequence == 0)
            {
                return;
            }
        }

        notes.Add("The local LSL inlet did not return the priming sample before measurement started.");
    }

    private static long? TryGetInt64(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => null
        };
    }

    private static long UnixTimeNanoseconds(DateTimeOffset value) =>
        (value.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks) * 100L;
}

public sealed record LslLocalRoundTripOptions(
    int Count = 16,
    int IntervalMilliseconds = 100,
    int TimeoutMilliseconds = 3000,
    int ResolveTimeoutMilliseconds = 5000,
    int WarmupMilliseconds = 300,
    string StreamName = "",
    string StreamType = LslDiagnosticDefaults.LocalLoopbackStreamType,
    string SourceId = "",
    string LslDllPath = "")
{
    public LslLocalRoundTripOptions Normalize() =>
        this with
        {
            Count = Math.Clamp(Count, 1, 1000),
            IntervalMilliseconds = Math.Clamp(IntervalMilliseconds, 0, 60_000),
            TimeoutMilliseconds = Math.Clamp(TimeoutMilliseconds, 100, 60_000),
            ResolveTimeoutMilliseconds = Math.Clamp(ResolveTimeoutMilliseconds, 100, 60_000),
            WarmupMilliseconds = Math.Clamp(WarmupMilliseconds, 0, 60_000),
            StreamName = StreamName?.Trim() ?? string.Empty,
            StreamType = string.IsNullOrWhiteSpace(StreamType) ? LslDiagnosticDefaults.LocalLoopbackStreamType : StreamType.Trim(),
            SourceId = SourceId?.Trim() ?? string.Empty,
            LslDllPath = LslDllPath?.Trim() ?? string.Empty
        };
}

public sealed record LslBrokerRoundTripOptions(
    int Count = 8,
    int IntervalMilliseconds = 250,
    int TimeoutMilliseconds = 5000,
    int ResolveTimeoutMilliseconds = 10000,
    int WarmupMilliseconds = 500,
    long SequenceStart = 1,
    string StreamName = LslDiagnosticDefaults.BrokerLatencyStreamName,
    string StreamType = LslDiagnosticDefaults.BrokerLatencyStreamType,
    string Path = "lsl_broker_roundtrip",
    int PayloadSizeBytes = 128,
    string BrokerHost = BrokerClientService.DefaultHost,
    int BrokerPort = BrokerClientService.DefaultPort,
    string LslDllPath = "")
{
    public LslBrokerRoundTripOptions Normalize() =>
        this with
        {
            Count = Math.Clamp(Count, 1, 1000),
            IntervalMilliseconds = Math.Clamp(IntervalMilliseconds, 0, 60_000),
            TimeoutMilliseconds = Math.Clamp(TimeoutMilliseconds, 100, 60_000),
            ResolveTimeoutMilliseconds = Math.Clamp(ResolveTimeoutMilliseconds, 100, 60_000),
            WarmupMilliseconds = Math.Clamp(WarmupMilliseconds, 0, 60_000),
            SequenceStart = SequenceStart <= 0 ? 1 : SequenceStart,
            StreamName = string.IsNullOrWhiteSpace(StreamName) ? LslDiagnosticDefaults.BrokerLatencyStreamName : StreamName.Trim(),
            StreamType = string.IsNullOrWhiteSpace(StreamType) ? LslDiagnosticDefaults.BrokerLatencyStreamType : StreamType.Trim(),
            Path = string.IsNullOrWhiteSpace(Path) ? "lsl_broker_roundtrip" : Path.Trim(),
            PayloadSizeBytes = Math.Clamp(PayloadSizeBytes, 0, 1_000_000),
            BrokerHost = string.IsNullOrWhiteSpace(BrokerHost) ? BrokerClientService.DefaultHost : BrokerHost.Trim(),
            BrokerPort = BrokerPort is > 0 and <= 65535 ? BrokerPort : BrokerClientService.DefaultPort,
            LslDllPath = LslDllPath?.Trim() ?? string.Empty
        };
}

public sealed record LslLocalRoundTripReport(
    DateTimeOffset CapturedAt,
    LslLocalRoundTripOptions Options,
    LslRuntimeState Runtime,
    LslTimeCorrectionSample? TimeCorrection,
    IReadOnlyList<LslLocalRoundTripSample> Samples,
    LslRoundTripSummary Summary,
    IReadOnlyList<string> Notes)
{
    public bool Succeeded => Runtime.Available && Summary.MatchedSamples == Options.Count;
}

public sealed record LslBrokerRoundTripReport(
    DateTimeOffset CapturedAt,
    LslBrokerRoundTripOptions Options,
    LslRuntimeState Runtime,
    LslTimeCorrectionSample? TimeCorrection,
    IReadOnlyList<LslBrokerRoundTripSample> Samples,
    LslRoundTripSummary Summary,
    IReadOnlyList<string> Notes)
{
    public bool Succeeded => Runtime.Available && Summary.MatchedSamples == Options.Count;
}

public sealed record LslLocalRoundTripSample(
    int Sequence,
    long HostSendUnixNs,
    double HostSendLslClockSeconds,
    double Value01,
    bool Matched,
    double? LslSampleTimestampSeconds,
    double HostReceiveLslClockSeconds,
    long HostReceiveUnixNs,
    double? LslCorrectedSampleToReceiveMs,
    double? HostSendToReceiveWallMs,
    double? TimeCorrectionOffsetMs,
    double? TimeCorrectionUncertaintyMs);

public sealed record LslBrokerRoundTripSample(
    long Sequence,
    long HostSendUnixNs,
    DateTimeOffset? WebSocketAckReceivedAt,
    bool MatchedLslSample,
    double? LslSampleTimestampSeconds,
    double HostReceiveLslClockSeconds,
    long HostReceiveUnixNs,
    long? BrokerReceiveUnixNs,
    long? BrokerPublishUnixNs,
    double? LslCorrectedSampleToReceiveMs,
    double? HostSendToLslReceiveWallMs,
    double? HostToBrokerReceiveMs,
    double? BrokerProcessingMs,
    double? TimeCorrectionOffsetMs,
    double? TimeCorrectionUncertaintyMs,
    JsonElement? WebSocketAckPayload,
    string? LslPayload,
    int WebSocketMessages);

public sealed record LslRoundTripSummary(
    int SampleCount,
    int MatchedSamples,
    double? MeanHostRoundTripMs,
    double? MinHostRoundTripMs,
    double? MaxHostRoundTripMs,
    double? MeanLslReceiveDelayMs,
    double? MeanTimeCorrectionUncertaintyMs,
    double? MeanBrokerProcessingMs)
{
    public static LslRoundTripSummary Empty { get; } = new(0, 0, null, null, null, null, null, null);

    public static LslRoundTripSummary FromLocal(IReadOnlyList<LslLocalRoundTripSample> samples) =>
        Build(
            samples.Count,
            samples.Count(static sample => sample.Matched),
            samples.Select(static sample => sample.HostSendToReceiveWallMs),
            samples.Select(static sample => sample.LslCorrectedSampleToReceiveMs),
            samples.Select(static sample => sample.TimeCorrectionUncertaintyMs),
            []);

    public static LslRoundTripSummary FromBroker(IReadOnlyList<LslBrokerRoundTripSample> samples) =>
        Build(
            samples.Count,
            samples.Count(static sample => sample.MatchedLslSample),
            samples.Select(static sample => sample.HostSendToLslReceiveWallMs),
            samples.Select(static sample => sample.LslCorrectedSampleToReceiveMs),
            samples.Select(static sample => sample.TimeCorrectionUncertaintyMs),
            samples.Select(static sample => sample.BrokerProcessingMs));

    private static LslRoundTripSummary Build(
        int sampleCount,
        int matchedSamples,
        IEnumerable<double?> hostRoundTripMs,
        IEnumerable<double?> lslReceiveDelayMs,
        IEnumerable<double?> uncertaintyMs,
        IEnumerable<double?> brokerProcessingMs)
    {
        var hostValues = hostRoundTripMs.Where(static value => value.HasValue).Select(static value => value!.Value).ToArray();
        return new LslRoundTripSummary(
            sampleCount,
            matchedSamples,
            Mean(hostValues),
            hostValues.Length == 0 ? null : hostValues.Min(),
            hostValues.Length == 0 ? null : hostValues.Max(),
            Mean(lslReceiveDelayMs),
            Mean(uncertaintyMs),
            Mean(brokerProcessingMs));
    }

    private static double? Mean(IEnumerable<double?> values)
    {
        var materialized = values.Where(static value => value.HasValue).Select(static value => value!.Value).ToArray();
        return materialized.Length == 0 ? null : materialized.Average();
    }

    private static double? Mean(IReadOnlyCollection<double> values) =>
        values.Count == 0 ? null : values.Average();
}
