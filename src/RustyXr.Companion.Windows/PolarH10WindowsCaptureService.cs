using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace RustyXr.Companion.Windows;

public sealed class PolarH10WindowsCaptureService
{
    public const string SessionSchema = "rusty.xr.companion.polar_session.v1";
    public const string BatterySchema = "rusty.xr.companion.polar_battery.v1";
    public const string PmdControlSchema = "rusty.xr.companion.polar_pmd_control.v1";
    public const string HeartRateSchema = "rusty.xr.companion.polar_hr_rr.v1";
    public const string AccFrameSchema = "rusty.xr.companion.polar_acc_frame.v1";
    public const string EcgFrameSchema = "rusty.xr.companion.polar_ecg_frame.v1";
    public const string MalformedFrameSchema = "rusty.xr.companion.polar_malformed_frame.v1";
    public const string PmdStreamAcc = "acc";
    public const string PmdStreamEcg = "ecg";
    public const string WindowsBleConnectionDefault = "default";
    public const string WindowsBleConnectionThroughputOptimized = "throughput-optimized";

    private const byte PmdMeasurementEcg = 0x00;
    private const byte PmdMeasurementAcc = 0x02;
    private const byte PmdOpcodeGetSettings = 0x01;
    private const byte PmdOpcodeStartStream = 0x02;
    private const byte PmdOpcodeStopStream = 0x03;
    private const byte PmdSettingSampleRate = 0x00;
    private const byte PmdSettingResolution = 0x01;
    private const byte PmdSettingRange = 0x02;

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
        return await CaptureAsync(options with { PmdStream = PmdStreamAcc }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PolarH10WindowsCaptureResult> CaptureAsync(
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

        var pmdStream = NormalizePmdStream(options.PmdStream);
        var pmdMeasurement = MeasurementForStream(pmdStream);
        var includePmd = options.IncludePmdAcc || options.IncludePmd;
        var accSampleRateHz = NormalizeAccSampleRate(options.AccSampleRateHz);
        var windowsBleMode = NormalizeWindowsBleConnectionMode(options.WindowsBleConnectionMode);

        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputJsonlPath) ?? ".");

        var startedAt = DateTimeOffset.UtcNow;
        var address = ParseBluetoothAddress(options.DeviceAddress);
        var formattedAddress = FormatAddress(address);
        long pmdFrameCount = 0L;
        long accFrameCount = 0L;
        long ecgFrameCount = 0L;
        long heartRateEventCount = 0L;
        long malformedFrameCount = 0L;
        var maxPduSize = 0;
        var connected = false;

        await using var writer = new PolarJsonlWriter(options.OutputJsonlPath);
        BluetoothLEDevice? device = null;
        GattSession? session = null;
        GattCharacteristic? pmdControl = null;
        var keepAlive = new List<object>();
        var unsubscribeActions = new List<Action>();
        var acceptingNotifications = true;

        try
        {
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(cancellationToken).ConfigureAwait(false);
            if (device is null)
            {
                throw new InvalidOperationException($"Polar device not found: {options.DeviceAddress}");
            }

            session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(cancellationToken).ConfigureAwait(false);
            session.MaintainConnection = true;
            maxPduSize = session.MaxPduSize;
            connected = true;
            var connectionParameterStatus = await TryRequestPreferredConnectionParametersAsync(
                    device,
                    windowsBleMode,
                    formattedAddress,
                    cancellationToken)
                .ConfigureAwait(false);

            await writer.WriteAsync(new PolarSessionRecord(
                    SessionSchema,
                    "connected",
                    UnixNowNs(),
                    formattedAddress,
                    device.Name,
                    maxPduSize,
                    null,
                    windowsBleMode,
                    connectionParameterStatus),
                cancellationToken).ConfigureAwait(false);

            await TryCaptureBatteryAsync(device, formattedAddress, writer, cancellationToken).ConfigureAwait(false);
            if (options.IncludeHeartRate)
            {
                await TrySubscribeHeartRateAsync(
                        device,
                        formattedAddress,
                        writer,
                        () => Interlocked.Increment(ref heartRateEventCount),
                        options.RecordObserver,
                        keepAlive,
                        unsubscribeActions,
                        () => acceptingNotifications,
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

            if (includePmd)
            {
                var pmdService = await GetServiceAsync(device, PolarGattIds.PmdService, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Polar PMD service not available.");
                pmdControl = await GetCharacteristicAsync(pmdService, PolarGattIds.PmdControlPoint, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Polar PMD control point not available.");
                var pmdData = await GetCharacteristicAsync(pmdService, PolarGattIds.PmdData, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Polar PMD data characteristic not available.");
                keepAlive.Add(pmdService);
                keepAlive.Add(pmdControl);
                keepAlive.Add(pmdData);

                TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> pmdControlHandler = async (_, eventArgs) =>
                {
                    if (!acceptingNotifications)
                    {
                        return;
                    }

                    var data = ReadBuffer(eventArgs.CharacteristicValue);
                    await writer.WriteAsync(new PolarPmdControlRecord(
                            PmdControlSchema,
                            UnixNowNs(),
                            formattedAddress,
                            Convert.ToHexString(data).ToLowerInvariant()),
                        CancellationToken.None).ConfigureAwait(false);
                };
                pmdControl.ValueChanged += pmdControlHandler;
                unsubscribeActions.Add(() => pmdControl.ValueChanged -= pmdControlHandler);
                await EnableNotificationsAsync(pmdControl, cancellationToken).ConfigureAwait(false);

                TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> pmdDataHandler = async (_, eventArgs) =>
                {
                    if (!acceptingNotifications)
                    {
                        return;
                    }

                    var data = ReadBuffer(eventArgs.CharacteristicValue);
                    var receivedUnixNs = UnixNowNs();
                    var ticks = Stopwatch.GetTimestamp();
                    try
                    {
                        var sequence = Interlocked.Read(ref pmdFrameCount);
                        object? frame = pmdStream == PmdStreamEcg
                            ? DecodeEcgFrame(data, sequence, receivedUnixNs, ticks, formattedAddress, device.Name)
                            : DecodeAccFrame(data, sequence, receivedUnixNs, ticks, formattedAddress, device.Name);
                        if (frame is not null)
                        {
                            Interlocked.Increment(ref pmdFrameCount);
                            if (frame is PolarAccFrameRecord)
                            {
                                Interlocked.Increment(ref accFrameCount);
                            }
                            else if (frame is PolarEcgFrameRecord)
                            {
                                Interlocked.Increment(ref ecgFrameCount);
                            }
                            await writer.WriteAsync(frame, CancellationToken.None).ConfigureAwait(false);
                            await NotifyRecordAsync(options.RecordObserver, frame, CancellationToken.None).ConfigureAwait(false);
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
                pmdData.ValueChanged += pmdDataHandler;
                unsubscribeActions.Add(() => pmdData.ValueChanged -= pmdDataHandler);
                await EnableNotificationsAsync(pmdData, cancellationToken).ConfigureAwait(false);

                await WritePmdAsync(pmdControl, [PmdOpcodeGetSettings, pmdMeasurement], cancellationToken).ConfigureAwait(false);
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                await WritePmdAsync(
                        pmdControl,
                        BuildStartPmdCommand(pmdStream, accSampleRateHz),
                        cancellationToken)
                    .ConfigureAwait(false);
                await writer.WriteAsync(
                        PolarSessionRecord.Info(
                            $"{pmdStream}_start_requested",
                            formattedAddress,
                            pmdStream == PmdStreamEcg
                                ? "ECG 130 Hz / 14 bit requested."
                                : $"ACC {accSampleRateHz} Hz / 16 bit / 8 g requested."),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await writer.WriteAsync(
                        PolarSessionRecord.Info("pmd_skipped", formattedAddress, "Polar PMD stream was not requested."),
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
                    await WritePmdAsync(pmdControl, [PmdOpcodeStopStream, pmdMeasurement], CancellationToken.None).ConfigureAwait(false);
                    await Task.Delay(300).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await writer.WriteAsync(
                            PolarSessionRecord.Warning($"{pmdStream}_stop_failed", formattedAddress, ex.Message),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }

            if (session is not null)
            {
                try
                {
                    maxPduSize = session.MaxPduSize;
                }
                catch (ObjectDisposedException)
                {
                }
            }

            acceptingNotifications = false;
            foreach (var unsubscribe in unsubscribeActions)
            {
                try
                {
                    unsubscribe();
                }
                catch
                {
                }
            }
            await Task.Delay(100).ConfigureAwait(false);

            await writer.WriteAsync(new PolarSessionRecord(
                    SessionSchema,
                    "completed",
                    UnixNowNs(),
                    formattedAddress,
                    device?.Name ?? string.Empty,
                    maxPduSize,
                    $"connected={connected}; include_hr={options.IncludeHeartRate}; include_pmd={includePmd}; pmd_stream={pmdStream}; hr_events={Interlocked.Read(ref heartRateEventCount)}; acc_frames={Interlocked.Read(ref accFrameCount)}; ecg_frames={Interlocked.Read(ref ecgFrameCount)}; malformed={Interlocked.Read(ref malformedFrameCount)}",
                    windowsBleMode,
                    null),
                CancellationToken.None).ConfigureAwait(false);

            session?.Dispose();
            device?.Dispose();
            GC.KeepAlive(keepAlive);
        }

        return new PolarH10WindowsCaptureResult(
            startedAt,
            DateTimeOffset.UtcNow,
            formattedAddress,
            device?.Name ?? string.Empty,
            connected,
            options.IncludeHeartRate,
            includePmd,
            pmdStream,
            accSampleRateHz,
            windowsBleMode,
            maxPduSize,
            Interlocked.Read(ref heartRateEventCount),
            Interlocked.Read(ref accFrameCount),
            Interlocked.Read(ref ecgFrameCount),
            Interlocked.Read(ref malformedFrameCount),
            options.OutputJsonlPath);
    }

    private static string NormalizePmdStream(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "ecg" or "bio:polar_ecg" or "polar_ecg" ? PmdStreamEcg : PmdStreamAcc;
    }

    private static byte MeasurementForStream(string pmdStream) =>
        pmdStream == PmdStreamEcg ? PmdMeasurementEcg : PmdMeasurementAcc;

    private static int NormalizeAccSampleRate(int value) =>
        value is 25 or 50 or 100 or 200 ? value : 200;

    private static string NormalizeWindowsBleConnectionMode(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Replace('_', '-');
        return normalized is "throughput" or "throughput-optimized" or "high-throughput"
            ? WindowsBleConnectionThroughputOptimized
            : WindowsBleConnectionDefault;
    }

    private static byte[] BuildStartPmdCommand(string pmdStream, int accSampleRateHz)
    {
        if (pmdStream == PmdStreamEcg)
        {
            return
            [
                PmdOpcodeStartStream,
                PmdMeasurementEcg,
                PmdSettingSampleRate,
                0x01,
                0x82,
                0x00,
                PmdSettingResolution,
                0x01,
                0x0E,
                0x00
            ];
        }

        return
        [
            PmdOpcodeStartStream,
            PmdMeasurementAcc,
            PmdSettingRange,
            0x01,
            0x08,
            0x00,
            PmdSettingSampleRate,
            0x01,
            (byte)(accSampleRateHz & 0xff),
            (byte)((accSampleRateHz >> 8) & 0xff),
            PmdSettingResolution,
            0x01,
            0x10,
            0x00
        ];
    }

    private static async Task<string> TryRequestPreferredConnectionParametersAsync(
        BluetoothLEDevice device,
        string mode,
        string deviceAddress,
        CancellationToken cancellationToken)
    {
        if (mode != WindowsBleConnectionThroughputOptimized)
        {
            return "not_requested";
        }

        try
        {
            var parametersType = Type.GetType("Windows.Devices.Bluetooth.BluetoothLEPreferredConnectionParameters, Microsoft.Windows.SDK.NET");
            if (parametersType is null)
            {
                return "unavailable: BluetoothLEPreferredConnectionParameters type not present";
            }

            var throughputProperty = parametersType.GetProperty("ThroughputOptimized");
            var parameters = throughputProperty?.GetValue(null);
            if (parameters is null)
            {
                return "unavailable: ThroughputOptimized property not present";
            }

            var method = device.GetType().GetMethod("RequestPreferredConnectionParameters", [parametersType]);
            if (method is null)
            {
                return "unavailable: RequestPreferredConnectionParameters method not present";
            }

            var operation = method.Invoke(device, [parameters]);
            if (operation is null)
            {
                return "not_started";
            }

            var statusProperty = operation.GetType().GetProperty("Status");
            var errorProperty = operation.GetType().GetProperty("ErrorCode");
            var getResultsMethod = operation.GetType().GetMethod("GetResults");
            for (var attempt = 0; attempt < 30; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var status = statusProperty?.GetValue(operation)?.ToString() ?? "unknown";
                if (!string.Equals(status, "Started", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
                    {
                        var result = getResultsMethod?.Invoke(operation, null);
                        return result is null ? "completed" : $"completed: {result}";
                    }

                    var error = errorProperty?.GetValue(operation);
                    return error is null ? status : $"{status}: {error}";
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            return $"started: request still pending for {deviceAddress}";
        }
        catch (Exception ex)
        {
            return $"failed: {ex.GetType().Name}: {ex.Message}";
        }
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
        Func<object, CancellationToken, ValueTask>? recordObserver,
        ICollection<object> keepAlive,
        ICollection<Action> unsubscribeActions,
        Func<bool> shouldAcceptNotifications,
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
        keepAlive.Add(hrService);
        keepAlive.Add(heartRate);

        TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler = async (_, eventArgs) =>
        {
            if (!shouldAcceptNotifications())
            {
                return;
            }

            var data = ReadBuffer(eventArgs.CharacteristicValue);
            var hr = DecodeHeartRate(data);
            if (hr is not null)
            {
                onHeartRate();
                var record = hr with
                    {
                        TimeUnixNs = UnixNowNs(),
                        ReceivedStopwatchTicks = Stopwatch.GetTimestamp(),
                        DeviceAddress = deviceAddress
                    };
                await writer.WriteAsync(record, CancellationToken.None).ConfigureAwait(false);
                await NotifyRecordAsync(recordObserver, record, CancellationToken.None).ConfigureAwait(false);
            }
        };
        heartRate.ValueChanged += handler;
        unsubscribeActions.Add(() => heartRate.ValueChanged -= handler);
        await EnableNotificationsAsync(heartRate, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask NotifyRecordAsync(
        Func<object, CancellationToken, ValueTask>? observer,
        object record,
        CancellationToken cancellationToken)
    {
        if (observer is not null)
        {
            await observer(record, cancellationToken).ConfigureAwait(false);
        }
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

    public static PolarEcgFrameRecord? DecodeEcgFrame(
        byte[] payload,
        long sequence,
        long unixNs,
        long ticks,
        string deviceAddress,
        string deviceName)
    {
        if (payload.Length < 10 || payload[0] != PmdMeasurementEcg)
        {
            return null;
        }

        var sensorTimestampNs = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(1, 8));
        var frameType = payload[9];
        var compressed = (frameType & 0x80) != 0;
        if (compressed || (frameType & 0x7f) != 0)
        {
            throw new InvalidOperationException("Compressed ECG frames are not decoded by this capture service.");
        }

        var payloadLength = payload.Length - 10;
        if (payloadLength <= 0 || payloadLength % 3 != 0)
        {
            throw new InvalidOperationException("ECG frame payload was not a multiple of signed 24-bit samples.");
        }

        var samples = new List<int>(payloadLength / 3);
        for (var offset = 10; offset + 2 < payload.Length; offset += 3)
        {
            samples.Add(ReadInt24LittleEndian(payload, offset));
        }

        if (samples.Count == 0)
        {
            throw new InvalidOperationException("ECG frame contained no decoded samples.");
        }

        return new PolarEcgFrameRecord(
            EcgFrameSchema,
            "windows_polar_ble",
            "ecg",
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
            samples[0],
            samples.Average(),
            samples.Min(),
            samples.Max(),
            samples);
    }

    private static int ReadInt24LittleEndian(byte[] payload, int offset)
    {
        var raw = payload[offset] | (payload[offset + 1] << 8) | (payload[offset + 2] << 16);
        if ((raw & 0x00800000) != 0)
        {
            raw |= unchecked((int)0xff000000);
        }

        return raw;
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
    bool IncludePmdAcc = true,
    bool IncludePmd = false,
    string PmdStream = PolarH10WindowsCaptureService.PmdStreamAcc,
    int AccSampleRateHz = 200,
    string WindowsBleConnectionMode = PolarH10WindowsCaptureService.WindowsBleConnectionDefault)
{
    [JsonIgnore]
    public Func<object, CancellationToken, ValueTask>? RecordObserver { get; init; }
}

public sealed record PolarH10WindowsCaptureResult(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string DeviceAddress,
    string DeviceName,
    bool Connected,
    bool IncludeHeartRate,
    bool IncludePmd,
    string PmdStream,
    int AccSampleRateHz,
    string WindowsBleConnectionMode,
    int MaxPduSize,
    long HeartRateEventCount,
    long AccFrameCount,
    long EcgFrameCount,
    long MalformedFrameCount,
    string OutputJsonlPath);

public sealed record PolarSessionRecord(
    string Schema,
    string Event,
    long TimeUnixNs,
    string DeviceAddress,
    string DeviceName,
    int MaxPduSize,
    string? Note,
    string? RequestedConnectionMode,
    string? RequestedConnectionStatus)
{
    public static PolarSessionRecord Info(string @event, string deviceAddress, string note) =>
        new(PolarH10WindowsCaptureService.SessionSchema, @event, NowNs(), deviceAddress, string.Empty, 0, note, null, null);

    public static PolarSessionRecord Warning(string @event, string deviceAddress, string note) =>
        new(PolarH10WindowsCaptureService.SessionSchema, @event, NowNs(), deviceAddress, string.Empty, 0, note, null, null);

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

public sealed record PolarEcgFrameRecord(
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
    int FirstMicrovolts,
    double MeanMicrovolts,
    int MinMicrovolts,
    int MaxMicrovolts,
    IReadOnlyList<int> SamplesMicrovolts);

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
