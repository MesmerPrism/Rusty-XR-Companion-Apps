using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RustyXr.Companion.Core;

public static class BrokerBioDiagnosticDefaults
{
    public const string PolarHeartRateStream = "bio:polar_hr_rr";
    public const string PolarEcgStream = "bio:polar_ecg";
    public const string PolarAccStream = "bio:polar_acc";
    public const string HeartRateServiceUuid = "0000180d-0000-1000-8000-00805f9b34fb";
    public const string HeartRateMeasurementUuid = "00002a37-0000-1000-8000-00805f9b34fb";
    public const string PolarPmdServiceUuid = "fb005c80-02e7-f387-1cad-8acd2d8df0c8";
    public const string PolarPmdControlPointUuid = "fb005c81-02e7-f387-1cad-8acd2d8df0c8";
    public const string PolarPmdDataUuid = "fb005c82-02e7-f387-1cad-8acd2d8df0c8";
    public const string PolarHeartRateLslType = "rusty.xr.polar.heart_rate";
    public const string PolarEcgLslType = "rusty.xr.polar.ecg";
    public const string PolarAccLslType = "rusty.xr.polar.acc";
    public const string StandardHeartRateGattProfile = "standard-heart-rate-service";
    public const string PolarPmdGattProfile = "polar-pmd-service";
}

public sealed class BrokerBioSimulationService
{
    public async Task<BrokerBioSimulationReport> RunAsync(
        BrokerBioSimulationOptions options,
        CancellationToken cancellationToken = default)
    {
        var normalized = options.Normalize();
        var eventsUri = BrokerClientService.CreateEventsUri(null, normalized.BrokerHost, normalized.BrokerPort);
        var brokerClient = new BrokerClientService();
        var samples = new List<BrokerBioSimulationSample>();
        var appVersion = AppBuildIdentity.Detect().DisplayLabel;

        for (var index = 0; index < normalized.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sequence = index + 1;
            var cycle = BuildCycle(sequence, normalized);
            foreach (var sample in cycle)
            {
                var probe = await brokerClient.SendCommandAsync(
                        eventsUri,
                        new BrokerCommandRequest(
                            "publish_stream_event",
                            $"bio-{sample.Kind}-{sequence.ToString(CultureInfo.InvariantCulture)}",
                            "rusty-xr-companion-cli",
                            "Rusty XR Companion CLI",
                            appVersion,
                            Parameters: new JsonObject
                            {
                                ["stream"] = sample.Stream,
                                ["sequence_id"] = sample.Sequence,
                                ["payload"] = sample.Payload
                            }),
                        TimeSpan.Zero,
                        8,
                        TimeSpan.FromSeconds(3),
                        cancellationToken)
                    .ConfigureAwait(false);

                samples.Add(sample with
                {
                    BrokerAccepted = probe.HasAcceptedAck,
                    WebSocketMessages = probe.ReceivedMessages.Count
                });
            }

            if (index + 1 < normalized.Count)
            {
                await Task.Delay(normalized.Interval, cancellationToken).ConfigureAwait(false);
            }
        }

        return new BrokerBioSimulationReport(DateTimeOffset.UtcNow, normalized, samples);
    }

    public static IReadOnlyList<BrokerBioSimulationSample> BuildCycle(
        int sequence,
        BrokerBioSimulationOptions options)
    {
        var samples = new List<BrokerBioSimulationSample>(3);
        var timestampNs = BrokerComparisonService.UnixTimeNanoseconds(DateTimeOffset.UtcNow);
        if (options.IncludeHeartRate)
        {
            var bpm = (ushort)Math.Clamp(options.BaseBpm + (sequence % 5) - 2, 40, 220);
            var rrMs = 60_000f / Math.Max(1, (int)bpm);
            var payloadBytes = SyntheticPolarPayloads.BuildHeartRateMeasurement(bpm, [rrMs]);
            samples.Add(new BrokerBioSimulationSample(
                "hr_rr",
                BrokerBioDiagnosticDefaults.PolarHeartRateStream,
                sequence,
                payloadBytes,
                HeartRatePayload(sequence, timestampNs, bpm, rrMs, payloadBytes),
                false,
                0));
        }

        if (options.IncludeEcg)
        {
            var ecgSamples = BuildEcgSamples(sequence, options.EcgSamplesPerFrame);
            var payloadBytes = SyntheticPolarPayloads.BuildEcgPmdFrame((ulong)timestampNs, ecgSamples);
            samples.Add(new BrokerBioSimulationSample(
                "ecg",
                BrokerBioDiagnosticDefaults.PolarEcgStream,
                sequence,
                payloadBytes,
                EcgPayload(sequence, timestampNs, ecgSamples, payloadBytes),
                false,
                0));
        }

        if (options.IncludeAcc)
        {
            var accSamples = BuildAccSamples(sequence, options.AccSamplesPerFrame);
            var payloadBytes = SyntheticPolarPayloads.BuildAccPmdFrame((ulong)timestampNs, accSamples);
            samples.Add(new BrokerBioSimulationSample(
                "acc",
                BrokerBioDiagnosticDefaults.PolarAccStream,
                sequence,
                payloadBytes,
                AccPayload(sequence, timestampNs, accSamples, payloadBytes),
                false,
                0));
        }

        return samples;
    }

    private static JsonObject HeartRatePayload(
        int sequence,
        long timestampNs,
        ushort bpm,
        float rrMs,
        byte[] payloadBytes) =>
        CommonPayload(
                "hr_rr",
                sequence,
                timestampNs,
                BrokerBioDiagnosticDefaults.HeartRateServiceUuid,
                BrokerBioDiagnosticDefaults.HeartRateMeasurementUuid,
                BrokerBioDiagnosticDefaults.StandardHeartRateGattProfile,
                payloadBytes)
            .AddObject(
                "decoded",
                new JsonObject
                {
                    ["bpm"] = bpm,
                    ["rr_ms"] = Math.Round(rrMs, 3),
                    ["sensor_contact"] = "supported_detected",
                    ["value01"] = 1.0
                })
            .AddObject(
                "lsl",
                new JsonObject
                {
                    ["stream_type"] = BrokerBioDiagnosticDefaults.PolarHeartRateLslType,
                    ["channel_count"] = 2,
                    ["channel_format"] = "float32",
                    ["channels"] = new JsonArray("bpm", "last_rr_ms")
                });

    private static JsonObject EcgPayload(
        int sequence,
        long timestampNs,
        IReadOnlyList<int> samplesMicrovolts,
        byte[] payloadBytes) =>
        CommonPayload(
                "ecg",
                sequence,
                timestampNs,
                BrokerBioDiagnosticDefaults.PolarPmdServiceUuid,
                BrokerBioDiagnosticDefaults.PolarPmdDataUuid,
                BrokerBioDiagnosticDefaults.PolarPmdGattProfile,
                payloadBytes)
            .AddObject(
                "pmd",
                new JsonObject
                {
                    ["control_point_uuid"] = BrokerBioDiagnosticDefaults.PolarPmdControlPointUuid,
                    ["data_uuid"] = BrokerBioDiagnosticDefaults.PolarPmdDataUuid,
                    ["measurement_type"] = 0,
                    ["frame_type"] = 0,
                    ["nominal_srate_hz"] = 130
                })
            .AddObject(
                "decoded",
                new JsonObject
                {
                    ["measurement_type"] = 0,
                    ["frame_type"] = 0,
                    ["sample_count"] = samplesMicrovolts.Count,
                    ["first_microvolts"] = samplesMicrovolts.Count > 0 ? samplesMicrovolts[0] : 0,
                    ["min_microvolts"] = samplesMicrovolts.Count > 0 ? samplesMicrovolts.Min() : 0,
                    ["max_microvolts"] = samplesMicrovolts.Count > 0 ? samplesMicrovolts.Max() : 0
                })
            .AddObject(
                "lsl",
                new JsonObject
                {
                    ["stream_type"] = BrokerBioDiagnosticDefaults.PolarEcgLslType,
                    ["nominal_srate_hz"] = 130,
                    ["channel_count"] = 1,
                    ["channel_format"] = "float32",
                    ["channels"] = new JsonArray("microvolts")
                });

    private static JsonObject AccPayload(
        int sequence,
        long timestampNs,
        IReadOnlyList<PolarAccSample> samples,
        byte[] payloadBytes)
    {
        var first = samples.Count > 0 ? samples[0] : new PolarAccSample(0, 0, 0);
        return CommonPayload(
                "acc",
                sequence,
                timestampNs,
                BrokerBioDiagnosticDefaults.PolarPmdServiceUuid,
                BrokerBioDiagnosticDefaults.PolarPmdDataUuid,
                BrokerBioDiagnosticDefaults.PolarPmdGattProfile,
                payloadBytes)
            .AddObject(
                "pmd",
                new JsonObject
                {
                    ["control_point_uuid"] = BrokerBioDiagnosticDefaults.PolarPmdControlPointUuid,
                    ["data_uuid"] = BrokerBioDiagnosticDefaults.PolarPmdDataUuid,
                    ["measurement_type"] = 2,
                    ["frame_type"] = 1,
                    ["nominal_srate_hz"] = 200
                })
            .AddObject(
                "decoded",
                new JsonObject
                {
                    ["measurement_type"] = 2,
                    ["frame_type"] = 1,
                    ["sample_count"] = samples.Count,
                    ["first_x_mg"] = first.XMg,
                    ["first_y_mg"] = first.YMg,
                    ["first_z_mg"] = first.ZMg
                })
            .AddObject(
                "lsl",
                new JsonObject
                {
                    ["stream_type"] = BrokerBioDiagnosticDefaults.PolarAccLslType,
                    ["nominal_srate_hz"] = 200,
                    ["channel_count"] = 3,
                    ["channel_format"] = "float32",
                    ["channels"] = new JsonArray("x_mg", "y_mg", "z_mg")
                });
    }

    private static JsonObject CommonPayload(
        string kind,
        int sequence,
        long timestampNs,
        string serviceUuid,
        string characteristicUuid,
        string gattProfile,
        byte[] payloadBytes) =>
        new()
        {
            ["source_transport"] = "synthetic-polar-gatt",
            ["diagnostic_transport"] = "broker-published-gatt-payload",
            ["ble_profile"] = gattProfile,
            ["kind"] = kind,
            ["sequence"] = sequence,
            ["client_send_time_unix_ns"] = timestampNs,
            ["service_uuid"] = serviceUuid,
            ["characteristic_uuid"] = characteristicUuid,
            ["gatt"] = new JsonObject
            {
                ["profile"] = gattProfile,
                ["service_uuid"] = serviceUuid,
                ["characteristic_uuid"] = characteristicUuid,
                ["notification_mode"] = "notify"
            },
            ["payload_base64"] = Convert.ToBase64String(payloadBytes),
            ["payload_length"] = payloadBytes.Length
        };

    private static int[] BuildEcgSamples(int sequence, int count)
    {
        var samples = new int[Math.Clamp(count, 1, 64)];
        for (var index = 0; index < samples.Length; index++)
        {
            var radians = (sequence * 0.7d) + index * 0.45d;
            samples[index] = (int)Math.Round(Math.Sin(radians) * 800d);
        }

        return samples;
    }

    private static PolarAccSample[] BuildAccSamples(int sequence, int count)
    {
        var samples = new PolarAccSample[Math.Clamp(count, 1, 64)];
        for (var index = 0; index < samples.Length; index++)
        {
            var radians = (sequence * 0.4d) + index * 0.25d;
            samples[index] = new PolarAccSample(
                (short)Math.Round(Math.Sin(radians) * 250d),
                (short)Math.Round(Math.Cos(radians) * 120d),
                (short)Math.Round(980d + Math.Sin(radians * 0.5d) * 40d));
        }

        return samples;
    }
}

public static class SyntheticPolarPayloads
{
    public static byte[] BuildHeartRateMeasurement(ushort bpm, IReadOnlyList<float> rrIntervalsMs)
    {
        var bytes = new List<byte> { 0x16, (byte)Math.Clamp((int)bpm, 0, 255) };
        foreach (var rrMs in rrIntervalsMs)
        {
            var raw = (ushort)Math.Clamp((int)Math.Round(rrMs * 1024f / 1000f), 0, ushort.MaxValue);
            bytes.Add((byte)(raw & 0xff));
            bytes.Add((byte)((raw >> 8) & 0xff));
        }

        return bytes.ToArray();
    }

    public static byte[] BuildEcgPmdFrame(ulong sensorTimestampNs, IReadOnlyList<int> samplesMicrovolts)
    {
        var bytes = BuildPmdHeader(0x00, sensorTimestampNs, 0x00);
        foreach (var value in samplesMicrovolts)
        {
            var clamped = Math.Clamp(value, -8_388_608, 8_388_607);
            bytes.Add((byte)(clamped & 0xff));
            bytes.Add((byte)((clamped >> 8) & 0xff));
            bytes.Add((byte)((clamped >> 16) & 0xff));
        }

        return bytes.ToArray();
    }

    public static byte[] BuildAccPmdFrame(ulong sensorTimestampNs, IReadOnlyList<PolarAccSample> samples)
    {
        var bytes = BuildPmdHeader(0x02, sensorTimestampNs, 0x01);
        foreach (var sample in samples)
        {
            WriteInt16LittleEndian(bytes, sample.XMg);
            WriteInt16LittleEndian(bytes, sample.YMg);
            WriteInt16LittleEndian(bytes, sample.ZMg);
        }

        return bytes.ToArray();
    }

    private static List<byte> BuildPmdHeader(byte measurementType, ulong sensorTimestampNs, byte frameType)
    {
        var bytes = new List<byte>(32) { measurementType };
        for (var index = 0; index < 8; index++)
        {
            bytes.Add((byte)((sensorTimestampNs >> (index * 8)) & 0xff));
        }

        bytes.Add(frameType);
        return bytes;
    }

    private static void WriteInt16LittleEndian(List<byte> bytes, short value)
    {
        bytes.Add((byte)(value & 0xff));
        bytes.Add((byte)((value >> 8) & 0xff));
    }
}

internal static class JsonObjectExtensions
{
    public static JsonObject AddObject(this JsonObject target, string propertyName, JsonObject value)
    {
        target[propertyName] = value;
        return target;
    }
}

public sealed record BrokerBioSimulationOptions(
    int Count = 4,
    int IntervalMilliseconds = 250,
    bool IncludeHeartRate = true,
    bool IncludeEcg = true,
    bool IncludeAcc = true,
    int BaseBpm = 72,
    int EcgSamplesPerFrame = 8,
    int AccSamplesPerFrame = 8,
    string BrokerHost = BrokerClientService.DefaultHost,
    int BrokerPort = BrokerClientService.DefaultPort)
{
    public TimeSpan Interval => TimeSpan.FromMilliseconds(IntervalMilliseconds);

    public BrokerBioSimulationOptions Normalize() =>
        this with
        {
            Count = Math.Clamp(Count, 1, 1000),
            IntervalMilliseconds = Math.Clamp(IntervalMilliseconds, 20, 60_000),
            BaseBpm = Math.Clamp(BaseBpm, 40, 220),
            EcgSamplesPerFrame = Math.Clamp(EcgSamplesPerFrame, 1, 64),
            AccSamplesPerFrame = Math.Clamp(AccSamplesPerFrame, 1, 64),
            BrokerHost = string.IsNullOrWhiteSpace(BrokerHost) ? BrokerClientService.DefaultHost : BrokerHost.Trim(),
            BrokerPort = BrokerPort is > 0 and <= 65535 ? BrokerPort : BrokerClientService.DefaultPort
        };
}

public sealed record BrokerBioSimulationReport(
    DateTimeOffset CapturedAt,
    BrokerBioSimulationOptions Options,
    IReadOnlyList<BrokerBioSimulationSample> Samples)
{
    public bool Succeeded => Samples.Count > 0 && Samples.All(static sample => sample.BrokerAccepted);
}

public sealed record BrokerBioSimulationSample(
    string Kind,
    string Stream,
    int Sequence,
    byte[] PayloadBytes,
    JsonObject Payload,
    bool BrokerAccepted,
    int WebSocketMessages);

public readonly record struct PolarAccSample(short XMg, short YMg, short ZMg);
