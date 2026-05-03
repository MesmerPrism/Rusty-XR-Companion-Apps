using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RustyXr.Companion.Core;

public static class BrokerDiagnosticDefaults
{
    public const string DriveAddress = "/rusty-xr/drive/radius";
    public const string DirectAcknowledgementAddress = "/rusty-xr/drive/ack";
    public const int DirectOscPort = 9001;
    public const int BrokerOscPort = OscService.DefaultPort;
    public const int DirectAcknowledgementPort = 19001;
}

public sealed class BrokerComparisonService
{
    public static OscMessage BuildDirectOscProbeMessage(
        string address,
        float value01,
        int sequence,
        long clientSendTimeUnixNs,
        int replyPort) =>
        new(
            string.IsNullOrWhiteSpace(address) ? BrokerDiagnosticDefaults.DriveAddress : address,
            [
                OscArgument.Float(Clamp01(value01)),
                OscArgument.Int(sequence),
                OscArgument.String(clientSendTimeUnixNs.ToString(CultureInfo.InvariantCulture)),
                OscArgument.Int(replyPort)
            ]);

    public static bool TryParseDirectOscAcknowledgement(
        OscMessage message,
        string expectedAddress,
        out DirectOscAcknowledgement acknowledgement)
    {
        acknowledgement = default;
        if (!string.Equals(
                message.Address,
                string.IsNullOrWhiteSpace(expectedAddress)
                    ? BrokerDiagnosticDefaults.DirectAcknowledgementAddress
                    : expectedAddress,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (message.Arguments.Count < 5 ||
            !TryGetInt64(message.Arguments[0], out var sequence) ||
            !TryGetInt64(message.Arguments[1], out var clientSendTimeUnixNs) ||
            !TryGetInt64(message.Arguments[2], out var targetReceiveTimeUnixNs) ||
            !TryGetInt64(message.Arguments[3], out var targetAckSendTimeUnixNs) ||
            !TryGetFloat(message.Arguments[4], out var value01))
        {
            return false;
        }

        var acceptedPulse =
            message.Arguments.Count > 5 &&
            message.Arguments[5].Kind == OscArgumentKind.Bool &&
            message.Arguments[5].BoolValue == true;

        acknowledgement = new DirectOscAcknowledgement(
            (int)Math.Clamp(sequence, int.MinValue, int.MaxValue),
            Clamp01(value01),
            clientSendTimeUnixNs,
            targetReceiveTimeUnixNs,
            targetAckSendTimeUnixNs,
            acceptedPulse);
        return true;
    }

    public async Task<BrokerComparisonReport> RunAsync(
        BrokerComparisonOptions options,
        CancellationToken cancellationToken = default)
    {
        var normalized = options.Normalize();
        var notes = new List<string>();
        DirectOscComparisonResult? directOsc = null;
        BrokerOscComparisonResult? brokerOsc = null;

        if (normalized.IncludeDirectOsc)
        {
            try
            {
                directOsc = await RunDirectOscAsync(normalized, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SocketException or InvalidOperationException or IOException or ArgumentException)
            {
                directOsc = DirectOscComparisonResult.Failed(normalized.Count, exception.Message);
            }
        }

        if (normalized.IncludeBrokerOsc)
        {
            try
            {
                brokerOsc = await RunBrokerOscAsync(normalized, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is WebSocketException or SocketException or InvalidOperationException or IOException or ArgumentException or JsonException)
            {
                brokerOsc = BrokerOscComparisonResult.Failed(normalized.Count, exception.Message);
            }
        }

        if (directOsc is null && brokerOsc is null)
        {
            notes.Add("No comparison route was selected.");
        }

        return new BrokerComparisonReport(DateTimeOffset.UtcNow, normalized, directOsc, brokerOsc, notes);
    }

    private static async Task<DirectOscComparisonResult> RunDirectOscAsync(
        BrokerComparisonOptions options,
        CancellationToken cancellationToken)
    {
        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, options.DirectAcknowledgementPort));
        var actualReplyPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;

        var pending = new ConcurrentDictionary<int, PendingProbe>();
        var samples = new ConcurrentQueue<BrokerComparisonRoundTripSample>();
        using var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        receiveCancellation.CancelAfter(options.TotalReceiveTimeout);
        var receiveTask = ReceiveDirectAcknowledgementsAsync(
            client,
            options.DirectAcknowledgementAddress,
            pending,
            samples,
            options.Count,
            receiveCancellation.Token);

        for (var index = 0; index < options.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sequence = index + 1;
            var value01 = ProbeValueForIndex(index);
            var sendTimeUnixNs = UnixTimeNanoseconds(DateTimeOffset.UtcNow);
            pending[sequence] = new PendingProbe(sequence, value01, sendTimeUnixNs);
            var message = BuildDirectOscProbeMessage(
                options.DirectOscAddress,
                value01,
                sequence,
                sendTimeUnixNs,
                actualReplyPort);
            var bytes = OscCodec.EncodeMessage(message);
            await client
                .SendAsync(bytes, bytes.Length, options.QuestHost, options.DirectOscPort)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            if (index + 1 < options.Count)
            {
                await Task.Delay(options.Interval, cancellationToken).ConfigureAwait(false);
            }
        }

        await receiveTask.ConfigureAwait(false);
        var orderedSamples = samples.OrderBy(static sample => sample.Sequence).ToArray();
        var notes = new List<string>();
        if (orderedSamples.Length < options.Count)
        {
            notes.Add($"Received {orderedSamples.Length} direct OSC acknowledgement(s) for {options.Count} sent probe(s).");
        }

        return new DirectOscComparisonResult(
            options.Count,
            orderedSamples.Length,
            actualReplyPort,
            orderedSamples.Count(static sample => sample.AcceptedPulse),
            BrokerComparisonClockAlignment.BuildSummary(orderedSamples),
            orderedSamples,
            notes);
    }

    private static async Task ReceiveDirectAcknowledgementsAsync(
        UdpClient client,
        string acknowledgementAddress,
        ConcurrentDictionary<int, PendingProbe> pending,
        ConcurrentQueue<BrokerComparisonRoundTripSample> samples,
        int expectedSamples,
        CancellationToken cancellationToken)
    {
        while (samples.Count < expectedSamples && !cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var receivedAtUnixNs = UnixTimeNanoseconds(DateTimeOffset.UtcNow);
            OscMessage message;
            try
            {
                message = OscCodec.DecodeMessage(result.Buffer);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (!TryParseDirectOscAcknowledgement(message, acknowledgementAddress, out var ack) ||
                !pending.TryGetValue(ack.Sequence, out var sent))
            {
                continue;
            }

            samples.Enqueue(BrokerComparisonClockAlignment.CreateSample(
                "direct_unity_osc_ack",
                ack.Sequence,
                ack.Value01,
                acceptedPulse: ack.AcceptedPulse,
                sent.ClientSendTimeUnixNs,
                ack.TargetReceiveTimeUnixNs,
                ack.TargetAckSendTimeUnixNs,
                receivedAtUnixNs));
        }
    }

    private static async Task<BrokerOscComparisonResult> RunBrokerOscAsync(
        BrokerComparisonOptions options,
        CancellationToken cancellationToken)
    {
        var client = new BrokerClientService();
        var eventsUri = BrokerClientService.CreateEventsUri(null, options.BrokerHost, options.BrokerPort);
        var stream = "osc:" + options.BrokerOscAddress;
        var notes = new List<string>();
        if (options.ConfigureBrokerOscIngress)
        {
            var configureProbe = await client.SendCommandAsync(
                    eventsUri,
                    new BrokerCommandRequest(
                        "configure_osc_ingress",
                        $"configure-osc-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}",
                        "rusty-xr-companion-cli",
                        "Rusty XR Companion CLI",
                        AppBuildIdentity.Detect().DisplayLabel,
                        Parameters: new JsonObject
                        {
                            ["enabled"] = true,
                            ["port"] = options.BrokerOscPort,
                            ["address"] = options.BrokerOscAddress
                        }),
                    TimeSpan.Zero,
                    8,
                    TimeSpan.FromSeconds(3),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!configureProbe.HasAcceptedAck)
            {
                notes.Add("Broker did not accept the configure_osc_ingress command before comparison.");
            }
        }

        var listenDuration = options.TotalReceiveTimeout;
        var subscribeTask = client.SendCommandAsync(
            eventsUri,
            new BrokerCommandRequest(
                "subscribe",
                $"compare-osc-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}",
                "rusty-xr-companion-cli",
                "Rusty XR Companion CLI",
                AppBuildIdentity.Detect().DisplayLabel,
                stream),
            listenDuration,
            options.MaxBrokerMessages,
            TimeSpan.FromSeconds(3),
            cancellationToken);

        await Task.Delay(options.BrokerSubscribeLead, cancellationToken).ConfigureAwait(false);

        var sent = new List<PendingProbe>();
        var osc = new OscService();
        for (var index = 0; index < options.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sequence = index + 1;
            var value01 = ProbeValueForIndex(index);
            var sendTimeUnixNs = UnixTimeNanoseconds(DateTimeOffset.UtcNow);
            sent.Add(new PendingProbe(sequence, value01, sendTimeUnixNs));
            await osc.SendAsync(
                    options.QuestHost,
                    options.BrokerOscPort,
                    new OscMessage(options.BrokerOscAddress, [OscArgument.Float(value01)]),
                    cancellationToken)
                .ConfigureAwait(false);

            if (index + 1 < options.Count)
            {
                await Task.Delay(options.Interval, cancellationToken).ConfigureAwait(false);
            }
        }

        var probe = await subscribeTask.ConfigureAwait(false);
        var samples = ExtractBrokerOscSamples(probe, stream, sent).ToArray();
        if (!probe.HasAcceptedAck)
        {
            notes.Add("Broker did not return an accepted subscription acknowledgement.");
        }

        if (samples.Length < options.Count)
        {
            notes.Add($"Received {samples.Length} broker OSC stream event(s) for {options.Count} sent probe(s).");
        }

        return new BrokerOscComparisonResult(
            options.Count,
            samples.Length,
            probe.ReceivedMessages.Count,
            BrokerComparisonClockAlignment.BuildSummary(samples),
            samples,
            notes);
    }

    private static IEnumerable<BrokerComparisonRoundTripSample> ExtractBrokerOscSamples(
        BrokerWebSocketProbeResult probe,
        string stream,
        IReadOnlyList<PendingProbe> sent)
    {
        var streamEvents = probe.ReceivedMessages
            .Where(message =>
                string.Equals(message.Type, "stream_event", StringComparison.Ordinal) &&
                message.Payload.ValueKind == JsonValueKind.Object &&
                message.Payload.TryGetProperty("stream", out var streamProperty) &&
                string.Equals(streamProperty.GetString(), stream, StringComparison.Ordinal))
            .ToArray();

        for (var index = 0; index < streamEvents.Length; index++)
        {
            var message = streamEvents[index];
            var brokerSequence = TryReadInt64(message.Payload, "sequence_id", out var sequence)
                ? sequence
                : index + 1;
            if (!TryReadInt64(message.Payload, "broker_time_unix_ns", out var brokerTimeUnixNs))
            {
                continue;
            }

            var sentProbe = index < sent.Count
                ? sent[index]
                : new PendingProbe((int)Math.Clamp(brokerSequence, int.MinValue, int.MaxValue), 0f, 0L);
            var value01 = TryReadPayloadFloat(message.Payload, "value01", out var payloadValue)
                ? payloadValue
                : sentProbe.Value01;

            yield return BrokerComparisonClockAlignment.CreateSample(
                "broker_osc_websocket_stream",
                (int)Math.Clamp(brokerSequence, int.MinValue, int.MaxValue),
                value01,
                acceptedPulse: false,
                sentProbe.ClientSendTimeUnixNs,
                brokerTimeUnixNs,
                brokerTimeUnixNs,
                UnixTimeNanoseconds(message.ReceivedAt));
        }
    }

    private static bool TryReadPayloadFloat(JsonElement payload, string propertyName, out float value)
    {
        value = 0f;
        if (!payload.TryGetProperty("payload", out var nested) ||
            nested.ValueKind != JsonValueKind.Object ||
            !nested.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetSingle(out value))
        {
            value = Clamp01(value);
            return true;
        }

        return false;
    }

    private static bool TryReadInt64(JsonElement payload, string propertyName, out long value)
    {
        value = 0L;
        return payload.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt64(out value);
    }

    private static bool TryGetFloat(OscArgument argument, out float value)
    {
        value = 0f;
        switch (argument.Kind)
        {
            case OscArgumentKind.Float when argument.FloatValue is float floatValue:
                value = Clamp01(floatValue);
                return true;
            case OscArgumentKind.Int when argument.IntValue is int intValue:
                value = Clamp01(intValue);
                return true;
            case OscArgumentKind.String when float.TryParse(argument.StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                value = Clamp01(parsed);
                return true;
            case OscArgumentKind.Bool:
                value = argument.BoolValue == true ? 1f : 0f;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetInt64(OscArgument argument, out long value)
    {
        value = 0L;
        switch (argument.Kind)
        {
            case OscArgumentKind.Int when argument.IntValue is int intValue:
                value = intValue;
                return true;
            case OscArgumentKind.Float when argument.FloatValue is float floatValue:
                value = (long)Math.Round(floatValue);
                return true;
            case OscArgumentKind.String:
                return long.TryParse(argument.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            case OscArgumentKind.Bool:
                value = argument.BoolValue == true ? 1L : 0L;
                return true;
            default:
                return false;
        }
    }

    private static float ProbeValueForIndex(int index) => index % 2 == 0 ? 1f : 0f;

    private static float Clamp01(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0f;
        }

        return Math.Clamp(value, 0f, 1f);
    }

    public static long UnixTimeNanoseconds(DateTimeOffset value) =>
        (value.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks) * 100L;

    private readonly record struct PendingProbe(
        int Sequence,
        float Value01,
        long ClientSendTimeUnixNs);
}

public static class BrokerComparisonClockAlignment
{
    public static BrokerComparisonRoundTripSample CreateSample(
        string route,
        int sequence,
        float value01,
        bool acceptedPulse,
        long hostSendTimeUnixNs,
        long targetReceiveTimeUnixNs,
        long targetSendTimeUnixNs,
        long hostReceiveTimeUnixNs)
    {
        var processingNs = Math.Max(0d, (double)targetSendTimeUnixNs - targetReceiveTimeUnixNs);
        var roundTripNs = Math.Max(0d, ((double)hostReceiveTimeUnixNs - hostSendTimeUnixNs) - processingNs);
        var targetMinusHostOffsetNs =
            (((double)targetReceiveTimeUnixNs - hostSendTimeUnixNs) +
             ((double)targetSendTimeUnixNs - hostReceiveTimeUnixNs)) / 2d;
        var hostToTargetAlignedNs = targetReceiveTimeUnixNs - targetMinusHostOffsetNs - hostSendTimeUnixNs;
        var targetToHostAlignedNs = hostReceiveTimeUnixNs - targetSendTimeUnixNs + targetMinusHostOffsetNs;

        return new BrokerComparisonRoundTripSample(
            route,
            sequence,
            Math.Clamp(value01, 0f, 1f),
            acceptedPulse,
            hostSendTimeUnixNs,
            targetReceiveTimeUnixNs,
            targetSendTimeUnixNs,
            hostReceiveTimeUnixNs,
            roundTripNs / 1_000_000d,
            targetMinusHostOffsetNs / 1_000_000d,
            hostToTargetAlignedNs / 1_000_000d,
            targetToHostAlignedNs / 1_000_000d);
    }

    public static BrokerComparisonClockAlignmentSummary BuildSummary(
        IReadOnlyList<BrokerComparisonRoundTripSample> samples)
    {
        if (samples.Count == 0)
        {
            return new BrokerComparisonClockAlignmentSummary(0, null, null, null, null, null, null, null, null);
        }

        var orderedByRoundTrip = samples.OrderBy(static sample => sample.RoundTripMs).ToArray();
        var bestWindowCount = Math.Max(1, (int)Math.Ceiling(orderedByRoundTrip.Length * 0.25d));
        var bestOffsets = orderedByRoundTrip
            .Take(bestWindowCount)
            .Select(static sample => sample.TargetMinusHostOffsetMs)
            .OrderBy(static value => value)
            .ToArray();
        var allOffsets = samples
            .Select(static sample => sample.TargetMinusHostOffsetMs)
            .OrderBy(static value => value)
            .ToArray();
        var allRoundTrips = samples
            .Select(static sample => sample.RoundTripMs)
            .OrderBy(static value => value)
            .ToArray();

        return new BrokerComparisonClockAlignmentSummary(
            samples.Count,
            Median(bestOffsets),
            Median(allOffsets),
            allOffsets.Average(),
            allRoundTrips.Average(),
            allRoundTrips.First(),
            allRoundTrips.Last(),
            samples.Average(static sample => sample.HostToTargetAlignedMs),
            samples.Average(static sample => sample.TargetToHostAlignedMs));
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var mid = values.Count / 2;
        return values.Count % 2 == 0
            ? (values[mid - 1] + values[mid]) / 2d
            : values[mid];
    }
}

public static class BrokerComparisonReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Write(BrokerComparisonReport report, string outputRoot)
    {
        var folder = Path.Combine(outputRoot, $"broker-compare-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "broker-comparison.json"), JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(Path.Combine(folder, "broker-comparison.md"), ToMarkdown(report));
        if (report.DirectOsc is not null)
        {
            File.WriteAllText(Path.Combine(folder, "direct-osc-roundtrip.csv"), ToCsv(report.DirectOsc.Samples));
        }

        if (report.BrokerOsc is not null)
        {
            File.WriteAllText(Path.Combine(folder, "broker-osc-stream.csv"), ToCsv(report.BrokerOsc.Samples));
        }

        return folder;
    }

    public static string ToMarkdown(BrokerComparisonReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Rusty XR Broker Comparison");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Captured: `{report.CapturedAt:O}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Quest host: `{report.Options.QuestHost}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Probe count: `{report.Options.Count}`");
        builder.AppendLine();
        AppendRoute(builder, "Direct Unity OSC", report.DirectOsc?.SentCount, report.DirectOsc?.AckCount, report.DirectOsc?.ClockAlignment, report.DirectOsc?.Notes);
        AppendRoute(builder, "Broker OSC/WebSocket", report.BrokerOsc?.SentCount, report.BrokerOsc?.StreamEventCount, report.BrokerOsc?.ClockAlignment, report.BrokerOsc?.Notes);
        if (report.Notes.Count > 0)
        {
            builder.AppendLine("## Notes");
            foreach (var note in report.Notes)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- {note}");
            }
        }

        return builder.ToString();
    }

    private static void AppendRoute(
        StringBuilder builder,
        string label,
        int? sent,
        int? received,
        BrokerComparisonClockAlignmentSummary? summary,
        IReadOnlyList<string>? notes)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"## {label}");
        if (summary is null)
        {
            builder.AppendLine("Not run.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"- Sent: `{sent ?? 0}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Received: `{received ?? 0}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Mean round-trip: `{FormatMs(summary.MeanRoundTripMs)}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- RTT span: `{FormatMs(summary.MinRoundTripMs)} .. {FormatMs(summary.MaxRoundTripMs)}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Recommended target-minus-host offset: `{FormatMs(summary.RecommendedTargetMinusHostOffsetMs)}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Mean host-to-target aligned: `{FormatMs(summary.MeanHostToTargetAlignedMs)}`");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Mean target-to-host aligned: `{FormatMs(summary.MeanTargetToHostAlignedMs)}`");
        if (notes is { Count: > 0 })
        {
            foreach (var note in notes)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- {note}");
            }
        }

        builder.AppendLine();
    }

    private static string ToCsv(IReadOnlyList<BrokerComparisonRoundTripSample> samples)
    {
        var builder = new StringBuilder();
        builder.AppendLine("route,sequence,value01,accepted_pulse,host_send_unix_ns,target_receive_unix_ns,target_send_unix_ns,host_receive_unix_ns,roundtrip_ms,target_minus_host_offset_ms,host_to_target_aligned_ms,target_to_host_aligned_ms");
        foreach (var sample in samples)
        {
            builder.AppendLine(string.Join(
                ",",
                Csv(sample.Route),
                sample.Sequence.ToString(CultureInfo.InvariantCulture),
                sample.Value01.ToString("0.######", CultureInfo.InvariantCulture),
                sample.AcceptedPulse ? "true" : "false",
                sample.HostSendTimeUnixNs.ToString(CultureInfo.InvariantCulture),
                sample.TargetReceiveTimeUnixNs.ToString(CultureInfo.InvariantCulture),
                sample.TargetSendTimeUnixNs.ToString(CultureInfo.InvariantCulture),
                sample.HostReceiveTimeUnixNs.ToString(CultureInfo.InvariantCulture),
                sample.RoundTripMs.ToString("0.###", CultureInfo.InvariantCulture),
                sample.TargetMinusHostOffsetMs.ToString("0.###", CultureInfo.InvariantCulture),
                sample.HostToTargetAlignedMs.ToString("0.###", CultureInfo.InvariantCulture),
                sample.TargetToHostAlignedMs.ToString("0.###", CultureInfo.InvariantCulture)));
        }

        return builder.ToString();
    }

    private static string FormatMs(double? value) =>
        value.HasValue ? value.Value.ToString("0.### ms", CultureInfo.InvariantCulture) : "n/a";

    private static string Csv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

public sealed record BrokerComparisonOptions(
    string QuestHost,
    int Count = 8,
    int IntervalMilliseconds = 250,
    int TimeoutMilliseconds = 5000,
    bool IncludeDirectOsc = true,
    bool IncludeBrokerOsc = true,
    string DirectOscAddress = BrokerDiagnosticDefaults.DriveAddress,
    int DirectOscPort = BrokerDiagnosticDefaults.DirectOscPort,
    string DirectAcknowledgementAddress = BrokerDiagnosticDefaults.DirectAcknowledgementAddress,
    int DirectAcknowledgementPort = BrokerDiagnosticDefaults.DirectAcknowledgementPort,
    string BrokerOscAddress = BrokerDiagnosticDefaults.DriveAddress,
    int BrokerOscPort = BrokerDiagnosticDefaults.BrokerOscPort,
    string BrokerHost = BrokerClientService.DefaultHost,
    int BrokerPort = BrokerClientService.DefaultPort,
    int BrokerSubscribeLeadMilliseconds = 500,
    int MaxBrokerMessages = 64,
    bool ConfigureBrokerOscIngress = true)
{
    public TimeSpan Interval => TimeSpan.FromMilliseconds(IntervalMilliseconds);
    public TimeSpan BrokerSubscribeLead => TimeSpan.FromMilliseconds(BrokerSubscribeLeadMilliseconds);
    public TimeSpan TotalReceiveTimeout => TimeSpan.FromMilliseconds(TimeoutMilliseconds + Count * IntervalMilliseconds);

    public BrokerComparisonOptions Normalize() =>
        this with
        {
            QuestHost = string.IsNullOrWhiteSpace(QuestHost) ? "127.0.0.1" : QuestHost.Trim(),
            Count = Math.Clamp(Count, 1, 1000),
            IntervalMilliseconds = Math.Clamp(IntervalMilliseconds, 20, 60_000),
            TimeoutMilliseconds = Math.Clamp(TimeoutMilliseconds, 250, 300_000),
            DirectOscAddress = NormalizeAddress(DirectOscAddress, BrokerDiagnosticDefaults.DriveAddress),
            DirectOscPort = NormalizePort(DirectOscPort, BrokerDiagnosticDefaults.DirectOscPort),
            DirectAcknowledgementAddress = NormalizeAddress(DirectAcknowledgementAddress, BrokerDiagnosticDefaults.DirectAcknowledgementAddress),
            DirectAcknowledgementPort = NormalizeOptionalPort(DirectAcknowledgementPort),
            BrokerOscAddress = NormalizeAddress(BrokerOscAddress, BrokerDiagnosticDefaults.DriveAddress),
            BrokerOscPort = NormalizePort(BrokerOscPort, BrokerDiagnosticDefaults.BrokerOscPort),
            BrokerHost = string.IsNullOrWhiteSpace(BrokerHost) ? BrokerClientService.DefaultHost : BrokerHost.Trim(),
            BrokerPort = NormalizePort(BrokerPort, BrokerClientService.DefaultPort),
            BrokerSubscribeLeadMilliseconds = Math.Clamp(BrokerSubscribeLeadMilliseconds, 0, 60_000),
            MaxBrokerMessages = Math.Clamp(MaxBrokerMessages, 4, 10_000)
        };

    private static string NormalizeAddress(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) || !value.Trim().StartsWith("/", StringComparison.Ordinal)
            ? fallback
            : value.Trim();

    private static int NormalizePort(int value, int fallback) => value is > 0 and <= 65535 ? value : fallback;

    private static int NormalizeOptionalPort(int value) => value is >= 0 and <= 65535 ? value : 0;
}

public sealed record BrokerComparisonReport(
    DateTimeOffset CapturedAt,
    BrokerComparisonOptions Options,
    DirectOscComparisonResult? DirectOsc,
    BrokerOscComparisonResult? BrokerOsc,
    IReadOnlyList<string> Notes)
{
    public bool Succeeded =>
        (DirectOsc is null || DirectOsc.Succeeded) &&
        (BrokerOsc is null || BrokerOsc.Succeeded) &&
        (DirectOsc is not null || BrokerOsc is not null);
}

public sealed record DirectOscComparisonResult(
    int SentCount,
    int AckCount,
    int LocalAckPort,
    int AcceptedPulseCount,
    BrokerComparisonClockAlignmentSummary ClockAlignment,
    IReadOnlyList<BrokerComparisonRoundTripSample> Samples,
    IReadOnlyList<string> Notes)
{
    public bool Succeeded => AckCount > 0;

    public static DirectOscComparisonResult Failed(int sentCount, string detail) =>
        new(
            sentCount,
            0,
            0,
            0,
            BrokerComparisonClockAlignment.BuildSummary([]),
            [],
            [detail]);
}

public sealed record BrokerOscComparisonResult(
    int SentCount,
    int StreamEventCount,
    int WebSocketMessageCount,
    BrokerComparisonClockAlignmentSummary ClockAlignment,
    IReadOnlyList<BrokerComparisonRoundTripSample> Samples,
    IReadOnlyList<string> Notes)
{
    public bool Succeeded => StreamEventCount > 0;

    public static BrokerOscComparisonResult Failed(int sentCount, string detail) =>
        new(
            sentCount,
            0,
            0,
            BrokerComparisonClockAlignment.BuildSummary([]),
            [],
            [detail]);
}

public readonly record struct DirectOscAcknowledgement(
    int Sequence,
    float Value01,
    long ClientSendTimeUnixNs,
    long TargetReceiveTimeUnixNs,
    long TargetAckSendTimeUnixNs,
    bool AcceptedPulse);

public sealed record BrokerComparisonRoundTripSample(
    string Route,
    int Sequence,
    float Value01,
    bool AcceptedPulse,
    long HostSendTimeUnixNs,
    long TargetReceiveTimeUnixNs,
    long TargetSendTimeUnixNs,
    long HostReceiveTimeUnixNs,
    double RoundTripMs,
    double TargetMinusHostOffsetMs,
    double HostToTargetAlignedMs,
    double TargetToHostAlignedMs);

public sealed record BrokerComparisonClockAlignmentSummary(
    int SampleCount,
    double? RecommendedTargetMinusHostOffsetMs,
    double? MedianTargetMinusHostOffsetMs,
    double? MeanTargetMinusHostOffsetMs,
    double? MeanRoundTripMs,
    double? MinRoundTripMs,
    double? MaxRoundTripMs,
    double? MeanHostToTargetAlignedMs,
    double? MeanTargetToHostAlignedMs);
