using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RustyXr.Companion.Core;

public sealed class LslStringOutletSession : IDisposable
{
    private readonly LslNativeRuntime.LslStringOutlet? _outlet;
    private long _sequence;

    public LslStringOutletSession(LslStringOutletOptions options)
    {
        Options = options.Normalize();
        Runtime = LslNativeRuntime.GetRuntimeState(Options.LslDllPath);
        if (!Runtime.Available)
        {
            return;
        }

        _outlet = LslNativeRuntime.CreateStringOutlet(
            Options.StreamName,
            Options.StreamType,
            Options.SourceId,
            channelCount: 1);
    }

    public LslStringOutletOptions Options { get; }

    public LslRuntimeState Runtime { get; }

    public bool Available => Runtime.Available && _outlet is not null;

    public LslStringForwardSample Push(string payload, long sourceUnixNs = 0, string stream = "", string schema = "")
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var pushUnixNs = UnixTimeNanoseconds(DateTimeOffset.UtcNow);
        var pushClock = Available ? LslNativeRuntime.LocalClock() : 0d;
        var payloadSize = Encoding.UTF8.GetByteCount(payload ?? string.Empty);
        if (!Available || _outlet is null)
        {
            return new LslStringForwardSample(
                sequence,
                sourceUnixNs,
                pushUnixNs,
                pushClock,
                stream,
                schema,
                payloadSize,
                false,
                Runtime.Detail,
                sourceUnixNs > 0 ? (pushUnixNs - sourceUnixNs) / 1_000_000d : null);
        }

        try
        {
            _outlet.Push([payload ?? string.Empty], pushClock);
            return new LslStringForwardSample(
                sequence,
                sourceUnixNs,
                pushUnixNs,
                pushClock,
                stream,
                schema,
                payloadSize,
                true,
                string.Empty,
                sourceUnixNs > 0 ? (pushUnixNs - sourceUnixNs) / 1_000_000d : null);
        }
        catch (Exception ex)
        {
            return new LslStringForwardSample(
                sequence,
                sourceUnixNs,
                pushUnixNs,
                pushClock,
                stream,
                schema,
                payloadSize,
                false,
                ex.Message,
                sourceUnixNs > 0 ? (pushUnixNs - sourceUnixNs) / 1_000_000d : null);
        }
    }

    public void Dispose() => _outlet?.Dispose();

    private static long UnixTimeNanoseconds(DateTimeOffset value) =>
        (value.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks) * 100L;
}

public sealed class LslStringDiagnosticsService
{
    public async Task<LslStringStreamCaptureReport> CaptureAsync(
        LslStringStreamCaptureOptions options,
        CancellationToken cancellationToken = default)
    {
        var normalized = options.Normalize();
        var runtime = LslNativeRuntime.GetRuntimeState(normalized.LslDllPath);
        if (!runtime.Available)
        {
            return new LslStringStreamCaptureReport(
                DateTimeOffset.UtcNow,
                normalized,
                runtime,
                null,
                [],
                LslStringStreamCaptureSummary.Empty,
                ["LSL runtime unavailable."]);
        }

        var samples = new List<LslStringStreamSample>(normalized.MaxSamples);
        var notes = new List<string>();
        using var inlet = LslNativeRuntime.ResolveStringInlet(
            normalized.ResolveProperty,
            normalized.ResolveValue,
            TimeSpan.FromMilliseconds(normalized.ResolveTimeoutMilliseconds));
        inlet.Open(TimeSpan.FromMilliseconds(normalized.TimeoutMilliseconds));
        var correction = LslNativeRuntime.GetTimeCorrection(
            inlet.Handle,
            TimeSpan.FromMilliseconds(normalized.TimeoutMilliseconds));
        if (normalized.WarmupMilliseconds > 0)
        {
            await Task.Delay(normalized.WarmupMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(normalized.DurationSeconds);
        while (samples.Count < normalized.MaxSamples && DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remainingMs = Math.Max(1, (int)(deadline - DateTimeOffset.UtcNow).TotalMilliseconds);
            var pulled = inlet.Pull(TimeSpan.FromMilliseconds(Math.Min(normalized.TimeoutMilliseconds, remainingMs)));
            if (pulled is null || pulled.Values.Length == 0)
            {
                continue;
            }

            var payload = pulled.Values[0] ?? string.Empty;
            var parsed = TryParse(payload);
            var stream = FirstString(parsed, "stream") ?? NestedString(parsed, "payload", "stream_id") ?? string.Empty;
            var type = FirstString(parsed, "type") ?? string.Empty;
            if (!Matches(normalized.RequiredType, type) || !Matches(normalized.RequiredStream, stream))
            {
                continue;
            }

            var receiveClock = LslNativeRuntime.LocalClock();
            var receiveUnixNs = UnixTimeNanoseconds(DateTimeOffset.UtcNow);
            var correctedSampleClock = pulled.TimestampSeconds + correction.OffsetSeconds;
            samples.Add(new LslStringStreamSample(
                samples.Count + 1,
                pulled.TimestampSeconds,
                receiveClock,
                receiveUnixNs,
                (receiveClock - correctedSampleClock) * 1000d,
                correction.OffsetSeconds * 1000d,
                correction.UncertaintySeconds * 1000d,
                type,
                stream,
                FirstInt64(parsed, "sequence_id"),
                FirstInt64(parsed, "broker_time_unix_ns"),
                FirstInt64(parsed, "broker_time_elapsed_ns"),
                NestedInt64(parsed, "payload", "sample_time_unix_ns"),
                NestedInt64(parsed, "payload", "sample_time_elapsed_ns"),
                NestedInt64(parsed, "payload", "broker_receive_time_unix_ns"),
                NestedInt64(parsed, "payload", "broker_receive_time_elapsed_ns"),
                NestedInt64(parsed, "payload", "sensor_timestamp_ns"),
                NestedInt64(parsed, "payload", "sample_count"),
                NestedString(parsed, "payload", "schema") ?? string.Empty,
                Encoding.UTF8.GetByteCount(payload),
                payload));
        }

        return new LslStringStreamCaptureReport(
            DateTimeOffset.UtcNow,
            normalized,
            runtime,
            correction,
            samples,
            LslStringStreamCaptureSummary.From(samples),
            notes);
    }

    private static bool Matches(string required, string actual) =>
        string.IsNullOrWhiteSpace(required) ||
        string.Equals(required.Trim(), actual, StringComparison.Ordinal);

    private static JsonElement? TryParse(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FirstString(JsonElement? element, string propertyName)
    {
        if (element is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static string? NestedString(JsonElement? element, string objectName, string propertyName)
    {
        if (element is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty(objectName, out var nested))
        {
            return null;
        }

        return FirstString(nested, propertyName);
    }

    private static long? FirstInt64(JsonElement? element, string propertyName)
    {
        if (element is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty(propertyName, out var property))
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

    private static long? NestedInt64(JsonElement? element, string objectName, string propertyName)
    {
        if (element is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty(objectName, out var nested))
        {
            return null;
        }

        return FirstInt64(nested, propertyName);
    }

    private static long UnixTimeNanoseconds(DateTimeOffset value) =>
        (value.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks) * 100L;
}

public sealed record LslStringOutletOptions(
    string StreamName = "rusty_xr_polar_windows_bridge",
    string StreamType = "rusty.xr.polar.diagnostic",
    string SourceId = "",
    string LslDllPath = "")
{
    public LslStringOutletOptions Normalize() =>
        this with
        {
            StreamName = string.IsNullOrWhiteSpace(StreamName) ? "rusty_xr_polar_windows_bridge" : StreamName.Trim(),
            StreamType = string.IsNullOrWhiteSpace(StreamType) ? "rusty.xr.polar.diagnostic" : StreamType.Trim(),
            SourceId = string.IsNullOrWhiteSpace(SourceId) ? $"rusty-xr-polar-windows-{Guid.NewGuid():N}" : SourceId.Trim(),
            LslDllPath = LslDllPath?.Trim() ?? string.Empty
        };
}

public sealed record LslStringForwardSample(
    long Sequence,
    long SourceUnixNs,
    long HostPushUnixNs,
    double HostPushLslClockSeconds,
    string Stream,
    string Schema,
    int PayloadSizeBytes,
    bool Pushed,
    string Error,
    double? SourceToPushMs);

public sealed record LslStringStreamCaptureOptions(
    int DurationSeconds = 15,
    int MaxSamples = 512,
    int TimeoutMilliseconds = 5000,
    int ResolveTimeoutMilliseconds = 10000,
    int WarmupMilliseconds = 500,
    string ResolveProperty = "name",
    string ResolveValue = LslDiagnosticDefaults.BrokerLatencyStreamName,
    string RequiredType = "stream_event",
    string RequiredStream = "",
    string LslDllPath = "")
{
    public LslStringStreamCaptureOptions Normalize() =>
        this with
        {
            DurationSeconds = Math.Clamp(DurationSeconds, 1, 300),
            MaxSamples = Math.Clamp(MaxSamples, 1, 100_000),
            TimeoutMilliseconds = Math.Clamp(TimeoutMilliseconds, 100, 60_000),
            ResolveTimeoutMilliseconds = Math.Clamp(ResolveTimeoutMilliseconds, 100, 60_000),
            WarmupMilliseconds = Math.Clamp(WarmupMilliseconds, 0, 60_000),
            ResolveProperty = string.IsNullOrWhiteSpace(ResolveProperty) ? "name" : ResolveProperty.Trim(),
            ResolveValue = string.IsNullOrWhiteSpace(ResolveValue) ? LslDiagnosticDefaults.BrokerLatencyStreamName : ResolveValue.Trim(),
            RequiredType = RequiredType?.Trim() ?? string.Empty,
            RequiredStream = RequiredStream?.Trim() ?? string.Empty,
            LslDllPath = LslDllPath?.Trim() ?? string.Empty
        };
}

public sealed record LslStringStreamCaptureReport(
    DateTimeOffset CapturedAt,
    LslStringStreamCaptureOptions Options,
    LslRuntimeState Runtime,
    LslTimeCorrectionSample? TimeCorrection,
    IReadOnlyList<LslStringStreamSample> Samples,
    LslStringStreamCaptureSummary Summary,
    IReadOnlyList<string> Notes)
{
    public bool Succeeded => Runtime.Available && Samples.Count > 0;
}

public sealed record LslStringStreamSample(
    int Index,
    double LslSampleTimestampSeconds,
    double HostReceiveLslClockSeconds,
    long HostReceiveUnixNs,
    double? LslCorrectedSampleToReceiveMs,
    double? TimeCorrectionOffsetMs,
    double? TimeCorrectionUncertaintyMs,
    string Type,
    string Stream,
    long? SequenceId,
    long? BrokerTimeUnixNs,
    long? BrokerTimeElapsedNs,
    long? SourceSampleUnixNs,
    long? SourceSampleElapsedNs,
    long? BrokerReceiveUnixNs,
    long? BrokerReceiveElapsedNs,
    long? SensorTimestampNs,
    long? SensorSampleCount,
    string PayloadSchema,
    int PayloadSizeBytes,
    string Payload);

public sealed record LslStringStreamCaptureSummary(
    int SampleCount,
    TimingCadenceSummary HostReceiveCadence,
    TimingCadenceSummary SourceSampleCadence,
    TimingCadenceSummary BrokerReceiveCadence,
    double? MeanLslReceiveDelayMs,
    double? MeanTimeCorrectionUncertaintyMs,
    double? MeanBrokerToHostWallMs,
    long SensorSampleCount,
    double? SensorSampleRateHz)
{
    public static LslStringStreamCaptureSummary Empty { get; } = new(
        0,
        TimingCadenceSummary.Empty,
        TimingCadenceSummary.Empty,
        TimingCadenceSummary.Empty,
        null,
        null,
        null,
        0,
        null);

    public static LslStringStreamCaptureSummary From(IReadOnlyList<LslStringStreamSample> samples)
    {
        var sensorSamples = samples
            .Select(static sample => sample.SensorSampleCount)
            .Where(static value => value.HasValue)
            .Sum(static value => value!.Value);
        var sourceCadence = TimingCadenceSummary.FromUnixNs(samples.Select(static sample => sample.SourceSampleUnixNs));
        double? sensorRate = sourceCadence.DurationMs is > 0
            ? sensorSamples / (sourceCadence.DurationMs.Value / 1000d)
            : null;
        return new LslStringStreamCaptureSummary(
            samples.Count,
            TimingCadenceSummary.FromUnixNs(samples.Select(static sample => (long?)sample.HostReceiveUnixNs)),
            sourceCadence,
            TimingCadenceSummary.FromUnixNs(samples.Select(static sample => sample.BrokerReceiveUnixNs)),
            Mean(samples.Select(static sample => sample.LslCorrectedSampleToReceiveMs)),
            Mean(samples.Select(static sample => sample.TimeCorrectionUncertaintyMs)),
            Mean(samples.Select(static sample =>
                sample.BrokerTimeUnixNs.HasValue
                    ? (sample.HostReceiveUnixNs - sample.BrokerTimeUnixNs.Value) / 1_000_000d
                    : (double?)null)),
            sensorSamples,
            sensorRate);
    }

    private static double? Mean(IEnumerable<double?> values)
    {
        var materialized = values.Where(static value => value.HasValue).Select(static value => value!.Value).ToArray();
        return materialized.Length == 0 ? null : materialized.Average();
    }
}

public sealed record TimingCadenceSummary(
    int Count,
    double? DurationMs,
    double? MeanIntervalMs,
    double? MinIntervalMs,
    double? MaxIntervalMs,
    double? RateHz)
{
    public static TimingCadenceSummary Empty { get; } = new(0, null, null, null, null, null);

    public static TimingCadenceSummary FromUnixNs(IEnumerable<long?> timestamps)
    {
        var values = timestamps
            .Where(static value => value.HasValue && value.Value > 0)
            .Select(static value => value!.Value)
            .Order()
            .ToArray();
        if (values.Length == 0)
        {
            return Empty;
        }

        if (values.Length == 1)
        {
            return new TimingCadenceSummary(1, 0, null, null, null, null);
        }

        var intervals = new double[values.Length - 1];
        for (var index = 1; index < values.Length; index++)
        {
            intervals[index - 1] = (values[index] - values[index - 1]) / 1_000_000d;
        }

        var durationMs = (values[^1] - values[0]) / 1_000_000d;
        return new TimingCadenceSummary(
            values.Length,
            durationMs,
            intervals.Average(),
            intervals.Min(),
            intervals.Max(),
            durationMs > 0 ? (values.Length - 1) / (durationMs / 1000d) : null);
    }
}
