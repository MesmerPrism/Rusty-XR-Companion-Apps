using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace RustyXr.Companion.Windows;

public sealed class PolarH10WindowsCaptureService
{
    public const string SessionSchema = "rusty.xr.companion.polar_session.v1";
    public const string BatterySchema = "rusty.xr.companion.polar_battery.v1";
    public const string PmdControlSchema = "rusty.xr.companion.polar_pmd_control.v1";
    public const string HeartRateSchema = "rusty.xr.companion.polar_hr_rr.v1";
    public const string AccFrameSchema = "rusty.xr.companion.polar_acc_frame.v1";
    public const string MalformedFrameSchema = "rusty.xr.companion.polar_malformed_frame.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<PolarH10WindowsCaptureResult> CaptureAccAsync(
        PolarH10WindowsCaptureOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.DeviceAddress))
        {
            throw new ArgumentException("Device address is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.OutputJsonlPath))
        {
            throw new ArgumentException("Output JSONL path is required.", nameof(options));
        }

        if (options.DurationSeconds is < 3 or > 300)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Duration must be 3..300 seconds.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputJsonlPath) ?? ".");

        var startedAt = DateTimeOffset.UtcNow;
        var address = ParseBluetoothAddress(options.DeviceAddress);
        var formattedAddress = FormatAddress(address);
        long accFrameCount = 0L;
        long heartRateEventCount = 0L;
        long malformedFrameCount = 0L;
        var connected = false;

        await using var writer = new PolarJsonlWriter(options.OutputJsonlPath);
        BluetoothLEDevice? device = null;
        GattSession? session = null;
        GattCharacteristic? pmdControl = null;

        try
        {
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(cancellationToken).ConfigureAwait(false);
            if (device is null)
            {
                throw new InvalidOperationException($"Polar device not found: {options.DeviceAddress}");
            }

            session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(cancellationToken).ConfigureAwait(false);
            session.MaintainConnection = true;
            connected = true;

            await writer.WriteAsync(new PolarSessionRecord(
                    SessionSchema,
                    "connected",
                    UnixNowNs(),
                    formattedAddress,
                    device.Name,
                    session.MaxPduSize,
                    null),
                cancellationToken).ConfigureAwait(false);

            await TryCaptureBatteryAsync(device, formattedAddress, writer, cancellationToken).ConfigureAwait(false);
            if (options.IncludeHeartRate)
            {
                await TrySubscribeHeartRateAsync(
                        device,
                        formattedAddress,
                        writer,
                        () => Interlocked.Increment(ref heartRateEventCount),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await writer.WriteAsync(
                        PolarSessionRecord.Info("heart_rate_skipped", formattedAddress, "Heart-rate/RR notifications were not requested."),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (options.IncludePmdAcc)
            {
                var pmdService = await GetServiceAsync(device, PolarGattIds.PmdService, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Polar PMD service not available.");
                pmdControl = await GetCharacteristicAsync(pmdService, PolarGattIds.PmdControlPoint, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Polar PMD control point not available.");
                var pmdData = await GetCharacteristicAsync(pmdService, PolarGattIds.PmdData, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Polar PMD data characteristic not available.");

                pmdControl.ValueChanged += async (_, eventArgs) =>
                {
                    var data = ReadBuffer(eventArgs.CharacteristicValue);
                    await writer.WriteAsync(new PolarPmdControlRecord(
                            PmdControlSchema,
                            UnixNowNs(),
                            formattedAddress,
                            Convert.ToHexString(data).ToLowerInvariant()),
                        CancellationToken.None).ConfigureAwait(false);
                };
                await EnableNotificationsAsync(pmdControl, cancellationToken).ConfigureAwait(false);

                pmdData.ValueChanged += async (_, eventArgs) =>
                {
                    var data = ReadBuffer(eventArgs.CharacteristicValue);
                    var receivedUnixNs = UnixNowNs();
                    var ticks = Stopwatch.GetTimestamp();
                    try
                    {
                        var sequence = Interlocked.Read(ref accFrameCount);
                        var frame = DecodeAccFrame(data, sequence, receivedUnixNs, ticks, formattedAddress, device.Name);
                        if (frame is not null)
                        {
                            Interlocked.Increment(ref accFrameCount);
                            await writer.WriteAsync(frame, CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref malformedFrameCount);
                        await writer.WriteAsync(new PolarMalformedFrameRecord(
                                MalformedFrameSchema,
                                receivedUnixNs,
                                formattedAddress,
                                Convert.ToHexString(data).ToLowerInvariant(),
                                ex.Message),
                            CancellationToken.None).ConfigureAwait(false);
                    }
                };
                await EnableNotificationsAsync(pmdData, cancellationToken).ConfigureAwait(false);

                await WritePmdAsync(pmdControl, [0x01, 0x02], cancellationToken).ConfigureAwait(false);
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                await WritePmdAsync(
                        pmdControl,
                        [0x02, 0x02, 0x02, 0x01, 0x08, 0x00, 0x00, 0x01, 0xC8, 0x00, 0x01, 0x01, 0x10, 0x00],
                        cancellationToken)
                    .ConfigureAwait(false);
                await writer.WriteAsync(
                        PolarSessionRecord.Info("acc_start_requested", formattedAddress, "ACC 200 Hz / 16 bit / 8 g requested."),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await writer.WriteAsync(
                        PolarSessionRecord.Info("pmd_acc_skipped", formattedAddress, "Polar PMD ACC was not requested."),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.DurationSeconds), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (pmdControl is not null)
            {
                try
                {
                    await WritePmdAsync(pmdControl, [0x03, 0x02], CancellationToken.None).ConfigureAwait(false);
                    await Task.Delay(300).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await writer.WriteAsync(
                            PolarSessionRecord.Warning("acc_stop_failed", formattedAddress, ex.Message),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }

            await writer.WriteAsync(new PolarSessionRecord(
                    SessionSchema,
                    "completed",
                    UnixNowNs(),
                    formattedAddress,
                    device?.Name ?? string.Empty,
                    session?.MaxPduSize ?? 0,
                    $"connected={connected}; include_hr={options.IncludeHeartRate}; include_pmd_acc={options.IncludePmdAcc}; hr_events={Interlocked.Read(ref heartRateEventCount)}; acc_frames={Interlocked.Read(ref accFrameCount)}; malformed={Interlocked.Read(ref malformedFrameCount)}"),
                CancellationToken.None).ConfigureAwait(false);

            session?.Dispose();
            device?.Dispose();
        }

        return new PolarH10WindowsCaptureResult(
            startedAt,
            DateTimeOffset.UtcNow,
            formattedAddress,
            device?.Name ?? string.Empty,
            connected,
            options.IncludeHeartRate,
            options.IncludePmdAcc,
            Interlocked.Read(ref heartRateEventCount),
            Interlocked.Read(ref accFrameCount),
            Interlocked.Read(ref malformedFrameCount),
            options.OutputJsonlPath);
    }

    private static async Task TryCaptureBatteryAsync(
        BluetoothLEDevice device,
        string deviceAddress,
        PolarJsonlWriter writer,
        CancellationToken cancellationToken)
    {
        var batteryService = await GetServiceAsync(device, PolarGattIds.BatteryService, cancellationToken).ConfigureAwait(false);
        if (batteryService is null)
        {
            return;
        }

        var batteryLevel = await GetCharacteristicAsync(batteryService, PolarGattIds.BatteryLevel, cancellationToken).ConfigureAwait(false);
        if (batteryLevel is null)
        {
            return;
        }

        try
        {
            var batteryPayload = await ReadBytesAsync(batteryLevel, cancellationToken).ConfigureAwait(false);
            if (batteryPayload.Length > 0)
            {
                await writer.WriteAsync(new PolarBatteryRecord(
                        BatterySchema,
                        UnixNowNs(),
                        deviceAddress,
                        batteryPayload[0]),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await writer.WriteAsync(
                    PolarSessionRecord.Warning("battery_read_failed", deviceAddress, ex.Message),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task TrySubscribeHeartRateAsync(
        BluetoothLEDevice device,
        string deviceAddress,
        PolarJsonlWriter writer,
        Action onHeartRate,
        CancellationToken cancellationToken)
    {
        var hrService = await GetServiceAsync(device, PolarGattIds.HeartRateService, cancellationToken).ConfigureAwait(false);
        if (hrService is null)
        {
            return;
        }

        var heartRate = await GetCharacteristicAsync(hrService, PolarGattIds.HeartRateMeasurement, cancellationToken).ConfigureAwait(false);
        if (heartRate is null)
        {
            return;
        }

        heartRate.ValueChanged += async (_, eventArgs) =>
        {
            var data = ReadBuffer(eventArgs.CharacteristicValue);
            var hr = DecodeHeartRate(data);
            if (hr is not null)
            {
                onHeartRate();
                await writer.WriteAsync(hr with
                    {
                        TimeUnixNs = UnixNowNs(),
                        ReceivedStopwatchTicks = Stopwatch.GetTimestamp(),
                        DeviceAddress = deviceAddress
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
        };
        await EnableNotificationsAsync(heartRate, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<GattDeviceService?> GetServiceAsync(
        BluetoothLEDevice device,
        Guid uuid,
        CancellationToken cancellationToken)
    {
        var result = await device.GetGattServicesForUuidAsync(uuid, BluetoothCacheMode.Uncached)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        return result.Status == GattCommunicationStatus.Success && result.Services.Count > 0
            ? result.Services[0]
            : null;
    }

    private static async Task<GattCharacteristic?> GetCharacteristicAsync(
        GattDeviceService service,
        Guid uuid,
        CancellationToken cancellationToken)
    {
        await service.RequestAccessAsync().AsTask(cancellationToken).ConfigureAwait(false);
        var result = await service.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Uncached)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        if (result.Status == GattCommunicationStatus.Success && result.Characteristics.Count > 0)
        {
            return result.Characteristics[0];
        }

        var cached = await service.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Cached)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        return cached.Status == GattCommunicationStatus.Success && cached.Characteristics.Count > 0
            ? cached.Characteristics[0]
            : null;
    }

    private static async Task EnableNotificationsAsync(GattCharacteristic characteristic, CancellationToken cancellationToken)
    {
        var value = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate)
            ? GattClientCharacteristicConfigurationDescriptorValue.Indicate
            : GattClientCharacteristicConfigurationDescriptorValue.Notify;
        var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(value)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        if (status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"Failed to enable {characteristic.Uuid}: {status}");
        }
    }

    private static async Task WritePmdAsync(
        GattCharacteristic characteristic,
        byte[] data,
        CancellationToken cancellationToken)
    {
        var writer = new DataWriter();
        writer.WriteBytes(data);
        var result = await characteristic.WriteValueWithResultAsync(writer.DetachBuffer())
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        if (result.Status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"PMD command write failed: {result.Status}");
        }
    }

    private static async Task<byte[]> ReadBytesAsync(GattCharacteristic characteristic, CancellationToken cancellationToken)
    {
        var result = await characteristic.ReadValueAsync().AsTask(cancellationToken).ConfigureAwait(false);
        if (result.Status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"Read failed: {result.Status}");
        }

        return ReadBuffer(result.Value);
    }

    private static byte[] ReadBuffer(IBuffer buffer)
    {
        var reader = DataReader.FromBuffer(buffer);
        var bytes = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(bytes);
        return bytes;
    }

    public static PolarAccFrameRecord? DecodeAccFrame(
        byte[] payload,
        long sequence,
        long unixNs,
        long ticks,
        string deviceAddress,
        string deviceName)
    {
        if (payload.Length < 10 || payload[0] != 0x02)
        {
            return null;
        }

        var sensorTimestampNs = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(1, 8));
        var frameType = payload[9];
        var compressed = (frameType & 0x80) != 0;
        if (compressed)
        {
            throw new InvalidOperationException("Compressed ACC frames are not decoded by this capture service.");
        }

        var samples = new List<int[]>(64);
        for (var offset = 10; offset + 5 < payload.Length; offset += 6)
        {
            var x = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(offset, 2));
            var y = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(offset + 2, 2));
            var z = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(offset + 4, 2));
            samples.Add([x, y, z]);
        }

        if (samples.Count == 0)
        {
            throw new InvalidOperationException("ACC frame contained no decoded samples.");
        }

        var xs = samples.Select(static sample => sample[0]).ToArray();
        var ys = samples.Select(static sample => sample[1]).ToArray();
        var zs = samples.Select(static sample => sample[2]).ToArray();
        return new PolarAccFrameRecord(
            AccFrameSchema,
            "windows_polar_ble",
            "acc",
            sequence,
            deviceAddress,
            deviceName,
            sensorTimestampNs,
            unixNs,
            ticks,
            samples.Count,
            frameType,
            compressed,
            payload.Length,
            xs[0],
            ys[0],
            zs[0],
            xs.Average(),
            ys.Average(),
            zs.Average(),
            xs.Min(),
            xs.Max(),
            ys.Min(),
            ys.Max(),
            zs.Min(),
            zs.Max(),
            samples);
    }

    public static PolarHeartRateRecord? DecodeHeartRate(byte[] payload)
    {
        if (payload.Length < 2)
        {
            return null;
        }

        var flags = payload[0];
        var index = 1;
        int bpm;
        if ((flags & 0x01) != 0)
        {
            if (payload.Length < 3)
            {
                return null;
            }

            bpm = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(index, 2));
            index += 2;
        }
        else
        {
            bpm = payload[index++];
        }

        if ((flags & 0x08) != 0)
        {
            index += 2;
        }

        var rr = new List<double>();
        if ((flags & 0x10) != 0)
        {
            while (index + 1 < payload.Length)
            {
                rr.Add(BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(index, 2)) * 1000.0 / 1024.0);
                index += 2;
            }
        }

        return new PolarHeartRateRecord(
            HeartRateSchema,
            0,
            0,
            string.Empty,
            bpm,
            rr);
    }

    public static ulong ParseBluetoothAddress(string value)
    {
        var compact = value.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        return ulong.Parse(compact, System.Globalization.NumberStyles.HexNumber);
    }

    public static string FormatAddress(ulong address)
    {
        var hex = address.ToString("X12");
        return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }

    private static long UnixNowNs()
    {
        var now = DateTimeOffset.UtcNow;
        return checked(now.ToUnixTimeMilliseconds() * 1_000_000L + (now.Ticks % TimeSpan.TicksPerMillisecond) * 100L);
    }

    private sealed class PolarJsonlWriter : IAsyncDisposable
    {
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public PolarJsonlWriter(string path)
        {
            _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
        }

        public async Task WriteAsync(object record, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _writer.WriteLineAsync(JsonSerializer.Serialize(record, JsonOptions)).ConfigureAwait(false);
                await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _writer.DisposeAsync().ConfigureAwait(false);
            _gate.Dispose();
        }
    }
}

public sealed record PolarH10WindowsCaptureOptions(
    string DeviceAddress,
    int DurationSeconds,
    string OutputJsonlPath,
    bool IncludeHeartRate = true,
    bool IncludePmdAcc = true);

public sealed record PolarH10WindowsCaptureResult(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string DeviceAddress,
    string DeviceName,
    bool Connected,
    bool IncludeHeartRate,
    bool IncludePmdAcc,
    long HeartRateEventCount,
    long AccFrameCount,
    long MalformedFrameCount,
    string OutputJsonlPath);

public sealed record PolarSessionRecord(
    string Schema,
    string Event,
    long TimeUnixNs,
    string DeviceAddress,
    string DeviceName,
    int Mtu,
    string? Note)
{
    public static PolarSessionRecord Info(string @event, string deviceAddress, string note) =>
        new(PolarH10WindowsCaptureService.SessionSchema, @event, NowNs(), deviceAddress, string.Empty, 0, note);

    public static PolarSessionRecord Warning(string @event, string deviceAddress, string note) =>
        new(PolarH10WindowsCaptureService.SessionSchema, @event, NowNs(), deviceAddress, string.Empty, 0, note);

    private static long NowNs()
    {
        var now = DateTimeOffset.UtcNow;
        return checked(now.ToUnixTimeMilliseconds() * 1_000_000L + (now.Ticks % TimeSpan.TicksPerMillisecond) * 100L);
    }
}

public sealed record PolarBatteryRecord(
    string Schema,
    long TimeUnixNs,
    string DeviceAddress,
    int BatteryPercent);

public sealed record PolarPmdControlRecord(
    string Schema,
    long TimeUnixNs,
    string DeviceAddress,
    string PayloadHex);

public sealed record PolarHeartRateRecord(
    string Schema,
    long TimeUnixNs,
    long ReceivedStopwatchTicks,
    string DeviceAddress,
    int HeartRateBpm,
    IReadOnlyList<double> RrIntervalsMs);

public sealed record PolarAccFrameRecord(
    string Schema,
    string Route,
    string StreamKind,
    long Sequence,
    string DeviceAddress,
    string DeviceName,
    ulong SensorTimestampNs,
    long PcBleReceivedUnixNs,
    long PcBleReceivedStopwatchTicks,
    int SampleCount,
    byte FrameType,
    bool Compressed,
    int PayloadSizeBytes,
    int FirstXMilliG,
    int FirstYMilliG,
    int FirstZMilliG,
    double MeanXMilliG,
    double MeanYMilliG,
    double MeanZMilliG,
    int MinXMilliG,
    int MaxXMilliG,
    int MinYMilliG,
    int MaxYMilliG,
    int MinZMilliG,
    int MaxZMilliG,
    IReadOnlyList<int[]> SamplesMilliG);

public sealed record PolarMalformedFrameRecord(
    string Schema,
    long TimeUnixNs,
    string DeviceAddress,
    string PayloadHex,
    string Error);

internal static class PolarGattIds
{
    public static readonly Guid BatteryService = Guid.Parse("0000180f-0000-1000-8000-00805f9b34fb");
    public static readonly Guid BatteryLevel = Guid.Parse("00002a19-0000-1000-8000-00805f9b34fb");
    public static readonly Guid HeartRateService = Guid.Parse("0000180d-0000-1000-8000-00805f9b34fb");
    public static readonly Guid HeartRateMeasurement = Guid.Parse("00002a37-0000-1000-8000-00805f9b34fb");
    public static readonly Guid PmdService = Guid.Parse("fb005c80-02e7-f387-1cad-8acd2d8df0c8");
    public static readonly Guid PmdControlPoint = Guid.Parse("fb005c81-02e7-f387-1cad-8acd2d8df0c8");
    public static readonly Guid PmdData = Guid.Parse("fb005c82-02e7-f387-1cad-8acd2d8df0c8");
}
