using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace RustyXr.Companion.Core;

public static class BrokerShellHelperDefaults
{
    public const string ExampleDirectoryName = "quest-broker-shell-helper";
    public const string HelperJarFileName = "rusty-xr-broker-shell-helper.jar";
    public const string DeviceJarPath = "/data/local/tmp/rusty-xr-broker-shell-helper.jar";
    public const string HelperMainClass = "com.example.rustyxr.shell.Helper";
    public const string SyntheticBinaryMagic = "RXYRVID1";
    public const int BinaryCodecH264 = 1;
    public const int BinaryCodecRawLuma8 = 2;
    public const int SyntheticBinaryHostPort = 18877;
    public const int SyntheticBinaryDevicePort = 8877;
    public const int SyntheticBinaryDefaultPacketCount = 3;
    public const int SyntheticBinaryDefaultPacketBytes = 1024;
    public const int SyntheticBinaryMaxPacketCount = 30;
    public const int BinaryStreamMaxPacketCount = 720;
    public const int SyntheticBinaryMaxPacketBytes = 65536;
    public const int BinaryStreamMaxPacketBytes = 1024 * 1024;
    public const int EncodedSyntheticDefaultFrames = 8;
    public const int EncodedSyntheticMaxFrames = 60;
    public const int EncodedSyntheticDefaultWidth = 640;
    public const int EncodedSyntheticDefaultHeight = 360;
    public const int EncodedSyntheticDefaultBitrateBps = 1_000_000;
    public const int ScreenrecordDefaultPacketBytes = 16 * 1024;
    public const int ScreenrecordDefaultTimeLimitSeconds = 1;
    public const int ScreenrecordMaxTimeLimitSeconds = 3;
}

public sealed class BrokerShellHelperService
{
    private readonly QuestAdbService _adbService;
    private readonly ICommandRunner _runner;

    public BrokerShellHelperService(QuestAdbService? adbService = null, ICommandRunner? runner = null)
    {
        _runner = runner ?? new CommandRunner();
        _adbService = adbService ?? new QuestAdbService(runner: _runner);
    }

    public static string ResolveRustyXrRoot(string? rustyXrRoot = null, string? currentDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(rustyXrRoot))
        {
            return Path.GetFullPath(rustyXrRoot);
        }

        return SourceWorkspaceGuide.Evaluate(currentDirectory: currentDirectory ?? Directory.GetCurrentDirectory()).RustyXrRepoPath;
    }

    public static string DefaultHelperJarPath(string rustyXrRoot) =>
        Path.Combine(
            rustyXrRoot,
            "examples",
            BrokerShellHelperDefaults.ExampleDirectoryName,
            "build",
            "outputs",
            BrokerShellHelperDefaults.HelperJarFileName);

    public static string DefaultBuildScriptPath(string rustyXrRoot) =>
        Path.Combine(
            rustyXrRoot,
            "examples",
            BrokerShellHelperDefaults.ExampleDirectoryName,
            "tools",
            "Build-BrokerShellHelper.ps1");

    public static string BuildAppProcessShellCommand(
        string deviceJarPath,
        string brokerHost,
        int brokerPort,
        bool disconnect,
        bool probeCodecs = false,
        bool probeCameras = false,
        bool probeCameraOpen = false,
        string? cameraOpenId = null,
        bool emitSyntheticVideoMetadata = false,
        int syntheticVideoSamples = 0,
        bool emitSyntheticVideoBinary = false,
        int syntheticVideoBinaryPort = BrokerShellHelperDefaults.SyntheticBinaryDevicePort,
        int syntheticVideoPackets = 0,
        int syntheticVideoPacketBytes = 0,
        bool emitMediaCodecSyntheticVideo = false,
        bool emitScreenrecordVideo = false,
        int encodedVideoFrames = BrokerShellHelperDefaults.EncodedSyntheticDefaultFrames,
        int encodedVideoWidth = BrokerShellHelperDefaults.EncodedSyntheticDefaultWidth,
        int encodedVideoHeight = BrokerShellHelperDefaults.EncodedSyntheticDefaultHeight,
        int encodedVideoBitrateBps = BrokerShellHelperDefaults.EncodedSyntheticDefaultBitrateBps,
        int screenrecordTimeLimitSeconds = BrokerShellHelperDefaults.ScreenrecordDefaultTimeLimitSeconds)
    {
        if (string.IsNullOrWhiteSpace(deviceJarPath) || !deviceJarPath.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("An absolute device jar path is required.", nameof(deviceJarPath));
        }

        if (brokerPort is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(brokerPort), "Broker port must be between 1 and 65535.");
        }
        if (syntheticVideoSamples is < 0 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(syntheticVideoSamples), "Synthetic video sample count must be between 0 and 30.");
        }
        if (syntheticVideoBinaryPort is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(syntheticVideoBinaryPort), "Synthetic binary video port must be between 1 and 65535.");
        }
        if (syntheticVideoPackets is < 0 or > BrokerShellHelperDefaults.SyntheticBinaryMaxPacketCount)
        {
            throw new ArgumentOutOfRangeException(nameof(syntheticVideoPackets), "Synthetic binary video packet count must be between 0 and 30.");
        }
        if (syntheticVideoPacketBytes is < 0 or > BrokerShellHelperDefaults.SyntheticBinaryMaxPacketBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(syntheticVideoPacketBytes), "Synthetic binary video packet bytes must be between 0 and 65536.");
        }
        if (encodedVideoFrames is <= 0 or > BrokerShellHelperDefaults.EncodedSyntheticMaxFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(encodedVideoFrames), "Encoded synthetic video frame count must be between 1 and 60.");
        }
        if (encodedVideoWidth is < 16 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(encodedVideoWidth), "Encoded synthetic video width must be between 16 and 4096.");
        }
        if (encodedVideoHeight is < 16 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(encodedVideoHeight), "Encoded synthetic video height must be between 16 and 4096.");
        }
        if (encodedVideoBitrateBps is < 1000 or > 100_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(encodedVideoBitrateBps), "Encoded synthetic video bitrate must be between 1000 and 100000000.");
        }
        if (screenrecordTimeLimitSeconds is <= 0 or > BrokerShellHelperDefaults.ScreenrecordMaxTimeLimitSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(screenrecordTimeLimitSeconds), "Screenrecord time limit must be between 1 and 3 seconds.");
        }

        var command =
            $"CLASSPATH={deviceJarPath} app_process / {BrokerShellHelperDefaults.HelperMainClass} " +
            $"--broker-host {ShellQuoteForDevice(brokerHost)} " +
            $"--broker-port {brokerPort.ToString(CultureInfo.InvariantCulture)}";
        if (disconnect)
        {
            command += " --disconnect";
        }
        if (probeCodecs)
        {
            command += " --probe-codecs";
        }
        if (probeCameras)
        {
            command += " --probe-cameras";
        }
        if (probeCameraOpen)
        {
            command += " --probe-camera-open";
            if (!string.IsNullOrWhiteSpace(cameraOpenId))
            {
                command += " --camera-open-id " + ShellQuoteForDevice(cameraOpenId.Trim());
            }
        }
        if (emitSyntheticVideoMetadata)
        {
            command += " --emit-synthetic-video-metadata";
        }
        if (syntheticVideoSamples > 0)
        {
            command += " --synthetic-video-samples " + syntheticVideoSamples.ToString(CultureInfo.InvariantCulture);
        }
        if (emitSyntheticVideoBinary)
        {
            command += " --emit-synthetic-video-binary";
            command += " --binary-video-port " + syntheticVideoBinaryPort.ToString(CultureInfo.InvariantCulture);
            if (syntheticVideoPackets > 0)
            {
                command += " --binary-video-packets " + syntheticVideoPackets.ToString(CultureInfo.InvariantCulture);
            }
            if (syntheticVideoPacketBytes > 0)
            {
                command += " --binary-video-packet-bytes " + syntheticVideoPacketBytes.ToString(CultureInfo.InvariantCulture);
            }
        }
        if (emitMediaCodecSyntheticVideo)
        {
            command += " --emit-mediacodec-synthetic-video";
            command += " --binary-video-port " + syntheticVideoBinaryPort.ToString(CultureInfo.InvariantCulture);
            command += " --encoded-video-frames " + encodedVideoFrames.ToString(CultureInfo.InvariantCulture);
            command += " --encoded-video-width " + encodedVideoWidth.ToString(CultureInfo.InvariantCulture);
            command += " --encoded-video-height " + encodedVideoHeight.ToString(CultureInfo.InvariantCulture);
            command += " --encoded-video-bitrate " + encodedVideoBitrateBps.ToString(CultureInfo.InvariantCulture);
        }
        if (emitScreenrecordVideo)
        {
            command += " --emit-screenrecord-video";
            command += " --binary-video-port " + syntheticVideoBinaryPort.ToString(CultureInfo.InvariantCulture);
            if (syntheticVideoPackets > 0)
            {
                command += " --binary-video-packets " + syntheticVideoPackets.ToString(CultureInfo.InvariantCulture);
            }
            if (syntheticVideoPacketBytes > 0)
            {
                command += " --binary-video-packet-bytes " + syntheticVideoPacketBytes.ToString(CultureInfo.InvariantCulture);
            }
            command += " --encoded-video-width " + encodedVideoWidth.ToString(CultureInfo.InvariantCulture);
            command += " --encoded-video-height " + encodedVideoHeight.ToString(CultureInfo.InvariantCulture);
            command += " --encoded-video-bitrate " + encodedVideoBitrateBps.ToString(CultureInfo.InvariantCulture);
            command += " --screenrecord-time-limit " + screenrecordTimeLimitSeconds.ToString(CultureInfo.InvariantCulture);
        }

        return command;
    }

    public async Task<CommandResult> BuildAsync(
        BrokerShellHelperBuildOptions options,
        CancellationToken cancellationToken = default)
    {
        var rustyXrRoot = ResolveRustyXrRoot(options.RustyXrRoot);
        var script = DefaultBuildScriptPath(rustyXrRoot);
        if (!File.Exists(script))
        {
            throw new FileNotFoundException("Broker shell-helper build script was not found.", script);
        }

        var arguments = "-NoProfile -ExecutionPolicy Bypass -File " + QuoteProcessArgument(script);
        if (!string.IsNullOrWhiteSpace(options.AndroidPlayerRoot))
        {
            arguments += " -AndroidPlayerRoot " + QuoteProcessArgument(options.AndroidPlayerRoot);
        }

        return await _runner.RunAsync("powershell", arguments, TimeSpan.FromMinutes(5), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<BrokerShellHelperRunResult> RunAsync(
        BrokerShellHelperRunOptions options,
        CancellationToken cancellationToken = default)
    {
        var normalized = options.Normalize();
        CommandResult? buildResult = null;
        if (normalized.BuildBeforeRun)
        {
            buildResult = await BuildAsync(
                    new BrokerShellHelperBuildOptions(normalized.RustyXrRoot, normalized.AndroidPlayerRoot),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!buildResult.Succeeded)
            {
                return new BrokerShellHelperRunResult(
                    normalized,
                    buildResult,
                    null,
                    null,
                    DateTimeOffset.UtcNow);
            }
        }

        var helperJarPath = normalized.HelperJarPath;
        if (!File.Exists(helperJarPath))
        {
            throw new FileNotFoundException("Broker shell-helper jar was not found. Build it first or pass --helper-jar.", helperJarPath);
        }

        var pushResult = await _adbService
            .PushFileAsync(normalized.Serial, helperJarPath, normalized.DeviceJarPath, cancellationToken)
            .ConfigureAwait(false);
        if (!pushResult.Succeeded)
        {
            return new BrokerShellHelperRunResult(
                normalized,
                buildResult,
                pushResult,
                null,
                DateTimeOffset.UtcNow);
        }

        var shellCommand = BuildAppProcessShellCommand(
            normalized.DeviceJarPath,
            normalized.BrokerHost,
            normalized.BrokerPort,
            normalized.Disconnect,
            normalized.ProbeCodecs,
            normalized.ProbeCameras,
            normalized.ProbeCameraOpen,
            normalized.CameraOpenId,
            normalized.EmitSyntheticVideoMetadata,
            normalized.SyntheticVideoSamples,
            normalized.EmitSyntheticVideoBinary,
            normalized.SyntheticVideoBinaryPort,
            normalized.SyntheticVideoPackets,
            normalized.SyntheticVideoPacketBytes,
            normalized.EmitMediaCodecSyntheticVideo,
            normalized.EmitScreenrecordVideo,
            normalized.EncodedVideoFrames,
            normalized.EncodedVideoWidth,
            normalized.EncodedVideoHeight,
            normalized.EncodedVideoBitrateBps,
            normalized.ScreenrecordTimeLimitSeconds);
        var launchResult = await _adbService
            .ShellAsync(normalized.Serial, shellCommand, cancellationToken)
            .ConfigureAwait(false);

        return new BrokerShellHelperRunResult(
            normalized,
            buildResult,
            pushResult,
            launchResult,
            DateTimeOffset.UtcNow);
    }

    public async Task<BrokerShellHelperBinaryProbeResult> RunBinaryProbeAsync(
        BrokerShellHelperBinaryProbeOptions options,
        CancellationToken cancellationToken = default)
    {
        var normalized = options.Normalize();
        CommandResult? buildResult = null;
        if (normalized.BuildBeforeRun)
        {
            buildResult = await BuildAsync(
                    new BrokerShellHelperBuildOptions(normalized.RustyXrRoot, normalized.AndroidPlayerRoot),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!buildResult.Succeeded)
            {
                return new BrokerShellHelperBinaryProbeResult(
                    normalized,
                    buildResult,
                    null,
                    null,
                    null,
                    null,
                    $"Shell-helper build failed: {buildResult.CondensedOutput}",
                    DateTimeOffset.UtcNow);
            }
        }

        var helperJarPath = normalized.HelperJarPath;
        if (!File.Exists(helperJarPath))
        {
            throw new FileNotFoundException("Broker shell-helper jar was not found. Build it first or pass --helper-jar.", helperJarPath);
        }

        var pushResult = await _adbService
            .PushFileAsync(normalized.Serial, helperJarPath, normalized.DeviceJarPath, cancellationToken)
            .ConfigureAwait(false);
        if (!pushResult.Succeeded)
        {
            return new BrokerShellHelperBinaryProbeResult(
                normalized,
                buildResult,
                pushResult,
                null,
                null,
                null,
                $"Shell-helper push failed: {pushResult.CondensedOutput}",
                DateTimeOffset.UtcNow);
        }

        var forwardResult = await _adbService
            .ForwardTcpAsync(normalized.Serial, normalized.HostPort, normalized.DevicePort, cancellationToken)
            .ConfigureAwait(false);
        if (!forwardResult.Succeeded)
        {
            return new BrokerShellHelperBinaryProbeResult(
                normalized,
                buildResult,
                pushResult,
                forwardResult,
                null,
                null,
                $"ADB forward failed: {forwardResult.CondensedOutput}",
                DateTimeOffset.UtcNow);
        }

        var shellCommand = BuildAppProcessShellCommand(
            normalized.DeviceJarPath,
            normalized.BrokerHost,
            normalized.BrokerPort,
            disconnect: false,
            probeCodecs: normalized.ProbeCodecs,
            probeCameras: normalized.ProbeCameras,
            probeCameraOpen: normalized.ProbeCameraOpen,
            cameraOpenId: normalized.CameraOpenId,
            emitSyntheticVideoMetadata: false,
            syntheticVideoSamples: 0,
            emitSyntheticVideoBinary: !normalized.UseMediaCodecSyntheticSource && !normalized.UseScreenrecordSource,
            emitScreenrecordVideo: normalized.UseScreenrecordSource,
            syntheticVideoBinaryPort: normalized.DevicePort,
            syntheticVideoPackets: normalized.PacketCount,
            syntheticVideoPacketBytes: normalized.PacketBytes,
            emitMediaCodecSyntheticVideo: normalized.UseMediaCodecSyntheticSource,
            encodedVideoFrames: normalized.EncodedVideoFrames,
            encodedVideoWidth: normalized.EncodedVideoWidth,
            encodedVideoHeight: normalized.EncodedVideoHeight,
            encodedVideoBitrateBps: normalized.EncodedVideoBitrateBps,
            screenrecordTimeLimitSeconds: normalized.ScreenrecordTimeLimitSeconds);

        var launchTask = _adbService.ShellAsync(normalized.Serial, shellCommand, cancellationToken);
        BrokerShellHelperBinaryStreamReport? streamReport = null;
        string error = string.Empty;
        try
        {
            streamReport = await ReceiveSyntheticBinaryStreamAsync(
                    normalized.ReceiverHost,
                    normalized.HostPort,
                    TimeSpan.FromMilliseconds(normalized.ReceiveTimeoutMilliseconds),
                    normalized.PayloadOutputPath,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or SocketException or TimeoutException or InvalidDataException)
        {
            error = exception.Message;
        }

        CommandResult? launchResult = null;
        try
        {
            launchResult = await launchTask.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException)
        {
            error = string.IsNullOrWhiteSpace(error) ? exception.Message : error + " " + exception.Message;
        }

        return new BrokerShellHelperBinaryProbeResult(
            normalized,
            buildResult,
            pushResult,
            forwardResult,
            launchResult,
            streamReport,
            error,
            DateTimeOffset.UtcNow);
    }

    public static Task<BrokerShellHelperBinaryStreamReport> ReceiveSyntheticBinaryStreamAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return ReceiveSyntheticBinaryStreamAsync(host, port, timeout, payloadOutputPath: null, cancellationToken);
    }

    public static async Task<BrokerShellHelperBinaryStreamReport> ReceiveSyntheticBinaryStreamAsync(
        string host,
        int port,
        TimeSpan timeout,
        string? payloadOutputPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Receiver host is required.", nameof(host));
        }
        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Receiver port must be between 1 and 65535.");
        }
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Receive timeout must be positive.");
        }

        var startedAt = Stopwatch.StartNew();
        Exception? lastError = null;
        var connectAttempts = 0;
        while (startedAt.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = timeout - startedAt.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            try
            {
                using var timeoutSource = new CancellationTokenSource(remaining);
                using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
                using var client = new TcpClient();
                connectAttempts++;
                var connectStarted = Stopwatch.GetTimestamp();
                await client.ConnectAsync(host, port, linkedSource.Token).ConfigureAwait(false);
                var connectElapsed = ElapsedMillisecondsSince(connectStarted);
                await using var stream = client.GetStream();
                var report = await ParseSyntheticBinaryStreamAsync(stream, payloadOutputPath, linkedSource.Token).ConfigureAwait(false);
                return report with
                {
                    HostConnectAttempts = connectAttempts,
                    HostConnectElapsedMilliseconds = connectElapsed,
                    HostReceiveDurationMilliseconds = (long)startedAt.Elapsed.TotalMilliseconds
                };
            }
            catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException)
            {
                lastError = exception;
                var delay = TimeSpan.FromMilliseconds(100);
                if (startedAt.Elapsed + delay > timeout)
                {
                    break;
                }
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException(
            $"Timed out waiting for synthetic binary video stream on {host}:{port}.",
            lastError);
    }

    public static BrokerShellHelperBinaryStreamReport ParseSyntheticBinaryStream(Stream stream)
    {
        return ParseSyntheticBinaryStream(stream, payloadOutputPath: null);
    }

    public static BrokerShellHelperBinaryStreamReport ParseSyntheticBinaryStream(
        Stream stream,
        string? payloadOutputPath)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return ParseSyntheticBinaryStreamCore(
            readExact: length => ReadExact(stream, length),
            DateTimeOffset.UtcNow,
            payloadOutputPath);
    }

    private static async Task<BrokerShellHelperBinaryStreamReport> ParseSyntheticBinaryStreamAsync(
        Stream stream,
        string? payloadOutputPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return await ParseSyntheticBinaryStreamCoreAsync(
                async length => await ReadExactAsync(stream, length, cancellationToken).ConfigureAwait(false),
                DateTimeOffset.UtcNow,
                payloadOutputPath)
            .ConfigureAwait(false);
    }

    private static BrokerShellHelperBinaryStreamReport ParseSyntheticBinaryStreamCore(
        Func<int, byte[]> readExact,
        DateTimeOffset capturedAt,
        string? payloadOutputPath)
    {
        return ParseSyntheticBinaryStreamCoreAsync(
                length => Task.FromResult(readExact(length)),
                capturedAt,
                payloadOutputPath)
            .GetAwaiter()
            .GetResult();
    }

    private static async Task<BrokerShellHelperBinaryStreamReport> ParseSyntheticBinaryStreamCoreAsync(
        Func<int, Task<byte[]>> readExact,
        DateTimeOffset capturedAt,
        string? payloadOutputPath)
    {
        var readStarted = Stopwatch.GetTimestamp();
        using var payloadArtifactWriter = BinaryPayloadArtifactWriter.Create(payloadOutputPath);
        var header = await readExact(32).ConfigureAwait(false);
        var magic = Encoding.ASCII.GetString(header.AsSpan(0, 8));
        if (!string.Equals(magic, BrokerShellHelperDefaults.SyntheticBinaryMagic, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unexpected synthetic binary video magic '{magic}'.");
        }

        var schemaVersion = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8, 4));
        var codecId = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(12, 4));
        var width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        var packetCount = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(24, 4));
        var declaredPacketBytes = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(28, 4));

        if (schemaVersion is not (1 or 2))
        {
            throw new InvalidDataException($"Unsupported synthetic binary video schema version {schemaVersion}.");
        }
        var codec = codecId switch
        {
            BrokerShellHelperDefaults.BinaryCodecH264 => "h264",
            BrokerShellHelperDefaults.BinaryCodecRawLuma8 => "raw_luma8",
            _ => throw new InvalidDataException($"Unsupported synthetic binary video codec id {codecId}.")
        };
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException($"Invalid synthetic binary video dimensions {width}x{height}.");
        }
        if (packetCount is <= 0 or > BrokerShellHelperDefaults.BinaryStreamMaxPacketCount)
        {
            throw new InvalidDataException($"Invalid synthetic binary video packet count {packetCount}.");
        }
        if (declaredPacketBytes is < 0 or > BrokerShellHelperDefaults.BinaryStreamMaxPacketBytes)
        {
            throw new InvalidDataException($"Invalid synthetic binary video packet byte count {declaredPacketBytes}.");
        }

        var packets = new List<BrokerShellHelperBinaryPacketReport>(packetCount);
        var h264Summary = new H264NalUnitSummaryBuilder();
        long totalPayloadBytes = 0;
        long totalWireBytes = header.Length;
        for (var index = 0; index < packetCount; index++)
        {
            var packetHeader = await readExact(16).ConfigureAwait(false);
            totalWireBytes += packetHeader.Length;
            var ptsUs = BinaryPrimitives.ReadInt64BigEndian(packetHeader.AsSpan(0, 8));
            var flags = BinaryPrimitives.ReadInt32BigEndian(packetHeader.AsSpan(8, 4));
            var sizeBytes = BinaryPrimitives.ReadInt32BigEndian(packetHeader.AsSpan(12, 4));
            long sourceElapsedNs = 0;
            long sourceUnixNs = 0;
            if (schemaVersion >= 2)
            {
                var timestampHeader = await readExact(16).ConfigureAwait(false);
                totalWireBytes += timestampHeader.Length;
                sourceElapsedNs = BinaryPrimitives.ReadInt64BigEndian(timestampHeader.AsSpan(0, 8));
                sourceUnixNs = BinaryPrimitives.ReadInt64BigEndian(timestampHeader.AsSpan(8, 8));
            }
            if (sizeBytes is <= 0 or > BrokerShellHelperDefaults.BinaryStreamMaxPacketBytes)
            {
                throw new InvalidDataException($"Invalid synthetic binary video packet size {sizeBytes} at packet {index}.");
            }
            if (declaredPacketBytes > 0 && sizeBytes != declaredPacketBytes)
            {
                throw new InvalidDataException(
                    $"Synthetic binary video packet {index} size {sizeBytes} did not match declared size {declaredPacketBytes}.");
            }

            var payload = await readExact(sizeBytes).ConfigureAwait(false);
            totalPayloadBytes += payload.Length;
            totalWireBytes += payload.Length;
            if (codecId == BrokerShellHelperDefaults.BinaryCodecH264)
            {
                h264Summary.Observe(payload);
            }
            payloadArtifactWriter?.Observe(payload);
            packets.Add(new BrokerShellHelperBinaryPacketReport(
                index,
                ptsUs,
                flags,
                sourceElapsedNs,
                sourceUnixNs,
                sizeBytes,
                ComputeChecksum(payload),
                payload[0],
                payload[^1]));
        }
        var payloadArtifact = payloadArtifactWriter?.Complete();

        return new BrokerShellHelperBinaryStreamReport(
            capturedAt,
            magic,
            schemaVersion,
            codecId,
            codec,
            width,
            height,
            packetCount,
            declaredPacketBytes,
            totalPayloadBytes,
            totalWireBytes,
            HostConnectAttempts: 0,
            HostConnectElapsedMilliseconds: 0,
            HostReadDurationMilliseconds: ElapsedMillisecondsSince(readStarted),
            HostReceiveDurationMilliseconds: ElapsedMillisecondsSince(readStarted),
            h264Summary.ToReport(),
            payloadArtifact,
            packets);
    }

    private static byte[] ReadExact(Stream stream, int length)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected EOF while reading synthetic binary video stream.");
            }
            offset += read;
        }
        return buffer;
    }

    private static async Task<byte[]> ReadExactAsync(
        Stream stream,
        int length,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected EOF while reading synthetic binary video stream.");
            }
            offset += read;
        }
        return buffer;
    }

    private static uint ComputeChecksum(ReadOnlySpan<byte> payload)
    {
        uint checksum = 0;
        foreach (var value in payload)
        {
            checksum = unchecked(checksum + value);
        }
        return checksum;
    }

    private static long ElapsedMillisecondsSince(long startTimestamp) =>
        Math.Max(0, (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);

    private sealed class BinaryPayloadArtifactWriter : IDisposable
    {
        private readonly string _destinationPath;
        private readonly string _temporaryPath;
        private readonly FileStream _stream;
        private readonly IncrementalHash _sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _completed;
        private long _byteCount;

        private BinaryPayloadArtifactWriter(string destinationPath, string temporaryPath, FileStream stream)
        {
            _destinationPath = destinationPath;
            _temporaryPath = temporaryPath;
            _stream = stream;
        }

        public static BinaryPayloadArtifactWriter? Create(string? destinationPath)
        {
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                return null;
            }

            var fullPath = Path.GetFullPath(destinationPath);
            if (string.IsNullOrWhiteSpace(Path.GetFileName(fullPath)))
            {
                throw new ArgumentException("Payload artifact path must include a file name.", nameof(destinationPath));
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read);
            return new BinaryPayloadArtifactWriter(fullPath, temporaryPath, stream);
        }

        public void Observe(ReadOnlySpan<byte> payload)
        {
            _stream.Write(payload);
            _sha256.AppendData(payload);
            _byteCount += payload.Length;
        }

        public BrokerShellHelperPayloadArtifact Complete()
        {
            _stream.Flush(flushToDisk: true);
            _stream.Dispose();
            var sha256 = Convert.ToHexString(_sha256.GetHashAndReset()).ToLowerInvariant();
            File.Move(_temporaryPath, _destinationPath, overwrite: true);
            _completed = true;
            return new BrokerShellHelperPayloadArtifact(_destinationPath, _byteCount, sha256);
        }

        public void Dispose()
        {
            _stream.Dispose();
            _sha256.Dispose();
            if (_completed || !File.Exists(_temporaryPath))
            {
                return;
            }

            try
            {
                File.Delete(_temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class H264NalUnitSummaryBuilder
    {
        private int _zeroRunLength;
        private bool _pendingNalHeader;
        private int _annexBStartCodeCount;
        private int _nalUnitCount;
        private int _spsCount;
        private int _ppsCount;
        private int _idrSliceCount;
        private int _nonIdrSliceCount;
        private int _seiCount;
        private int _audCount;

        public void Observe(ReadOnlySpan<byte> payload)
        {
            foreach (var value in payload)
            {
                if (_pendingNalHeader)
                {
                    CountNalHeader(value);
                    _pendingNalHeader = false;
                    _zeroRunLength = 0;
                    continue;
                }

                if (value == 0)
                {
                    _zeroRunLength++;
                    continue;
                }

                if (value == 1 && _zeroRunLength >= 2)
                {
                    _annexBStartCodeCount++;
                    _pendingNalHeader = true;
                    _zeroRunLength = 0;
                    continue;
                }

                _zeroRunLength = 0;
            }
        }

        public H264NalUnitSummary ToReport() =>
            new(
                _annexBStartCodeCount,
                _nalUnitCount,
                _spsCount,
                _ppsCount,
                _idrSliceCount,
                _nonIdrSliceCount,
                _seiCount,
                _audCount);

        private void CountNalHeader(byte header)
        {
            var type = header & 0x1f;
            _nalUnitCount++;
            switch (type)
            {
                case 1:
                    _nonIdrSliceCount++;
                    break;
                case 5:
                    _idrSliceCount++;
                    break;
                case 6:
                    _seiCount++;
                    break;
                case 7:
                    _spsCount++;
                    break;
                case 8:
                    _ppsCount++;
                    break;
                case 9:
                    _audCount++;
                    break;
            }
        }
    }

    private static string ShellQuoteForDevice(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "'127.0.0.1'";
        }

        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    private static string QuoteProcessArgument(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

public sealed record BrokerShellHelperBuildOptions(
    string? RustyXrRoot = null,
    string? AndroidPlayerRoot = null);

public sealed record BrokerShellHelperRunOptions(
    string Serial,
    string? RustyXrRoot = null,
    string? HelperJarPath = null,
    string DeviceJarPath = BrokerShellHelperDefaults.DeviceJarPath,
    string BrokerHost = BrokerClientService.DefaultHost,
    int BrokerPort = BrokerClientService.DefaultPort,
    bool BuildBeforeRun = true,
    bool Disconnect = false,
    bool ProbeCodecs = false,
    bool ProbeCameras = false,
    bool ProbeCameraOpen = false,
    string CameraOpenId = "",
    bool EmitSyntheticVideoMetadata = false,
    int SyntheticVideoSamples = 0,
    bool EmitSyntheticVideoBinary = false,
    int SyntheticVideoBinaryPort = BrokerShellHelperDefaults.SyntheticBinaryDevicePort,
    int SyntheticVideoPackets = 0,
    int SyntheticVideoPacketBytes = 0,
    bool EmitMediaCodecSyntheticVideo = false,
    bool EmitScreenrecordVideo = false,
    int EncodedVideoFrames = BrokerShellHelperDefaults.EncodedSyntheticDefaultFrames,
    int EncodedVideoWidth = BrokerShellHelperDefaults.EncodedSyntheticDefaultWidth,
    int EncodedVideoHeight = BrokerShellHelperDefaults.EncodedSyntheticDefaultHeight,
    int EncodedVideoBitrateBps = BrokerShellHelperDefaults.EncodedSyntheticDefaultBitrateBps,
    int ScreenrecordTimeLimitSeconds = BrokerShellHelperDefaults.ScreenrecordDefaultTimeLimitSeconds,
    string? AndroidPlayerRoot = null)
{
    public BrokerShellHelperRunOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(Serial))
        {
            throw new ArgumentException("Device serial is required.", nameof(Serial));
        }

        var root = BrokerShellHelperService.ResolveRustyXrRoot(RustyXrRoot);
        return this with
        {
            RustyXrRoot = root,
            HelperJarPath = string.IsNullOrWhiteSpace(HelperJarPath)
                ? BrokerShellHelperService.DefaultHelperJarPath(root)
                : Path.GetFullPath(HelperJarPath),
            DeviceJarPath = string.IsNullOrWhiteSpace(DeviceJarPath)
                ? BrokerShellHelperDefaults.DeviceJarPath
                : DeviceJarPath,
            BrokerHost = string.IsNullOrWhiteSpace(BrokerHost)
                ? BrokerClientService.DefaultHost
                : BrokerHost.Trim(),
            BrokerPort = BrokerPort is > 0 and <= 65535 ? BrokerPort : BrokerClientService.DefaultPort,
            CameraOpenId = (CameraOpenId ?? string.Empty).Trim(),
            SyntheticVideoSamples = SyntheticVideoSamples is >= 0 and <= 30 ? SyntheticVideoSamples : 0,
            SyntheticVideoBinaryPort = SyntheticVideoBinaryPort is > 0 and <= 65535
                ? SyntheticVideoBinaryPort
                : BrokerShellHelperDefaults.SyntheticBinaryDevicePort,
            SyntheticVideoPackets = SyntheticVideoPackets is >= 0 and <= BrokerShellHelperDefaults.SyntheticBinaryMaxPacketCount
                ? SyntheticVideoPackets
                : 0,
            SyntheticVideoPacketBytes = SyntheticVideoPacketBytes is >= 0 and <= BrokerShellHelperDefaults.SyntheticBinaryMaxPacketBytes
                ? SyntheticVideoPacketBytes
                : 0,
            EncodedVideoFrames = EncodedVideoFrames is > 0 and <= BrokerShellHelperDefaults.EncodedSyntheticMaxFrames
                ? EncodedVideoFrames
                : BrokerShellHelperDefaults.EncodedSyntheticDefaultFrames,
            EncodedVideoWidth = EncodedVideoWidth is >= 16 and <= 4096
                ? EncodedVideoWidth
                : BrokerShellHelperDefaults.EncodedSyntheticDefaultWidth,
            EncodedVideoHeight = EncodedVideoHeight is >= 16 and <= 4096
                ? EncodedVideoHeight
                : BrokerShellHelperDefaults.EncodedSyntheticDefaultHeight,
            EncodedVideoBitrateBps = EncodedVideoBitrateBps is >= 1000 and <= 100_000_000
                ? EncodedVideoBitrateBps
                : BrokerShellHelperDefaults.EncodedSyntheticDefaultBitrateBps,
            ScreenrecordTimeLimitSeconds = ScreenrecordTimeLimitSeconds is > 0 and <= BrokerShellHelperDefaults.ScreenrecordMaxTimeLimitSeconds
                ? ScreenrecordTimeLimitSeconds
                : BrokerShellHelperDefaults.ScreenrecordDefaultTimeLimitSeconds
        };
    }
}

public sealed record BrokerShellHelperBinaryProbeOptions(
    string Serial,
    string? RustyXrRoot = null,
    string? HelperJarPath = null,
    string DeviceJarPath = BrokerShellHelperDefaults.DeviceJarPath,
    string BrokerHost = BrokerClientService.DefaultHost,
    int BrokerPort = BrokerClientService.DefaultPort,
    bool BuildBeforeRun = true,
    bool ProbeCodecs = false,
    bool ProbeCameras = false,
    bool ProbeCameraOpen = false,
    string CameraOpenId = "",
    int HostPort = BrokerShellHelperDefaults.SyntheticBinaryHostPort,
    int DevicePort = BrokerShellHelperDefaults.SyntheticBinaryDevicePort,
    int PacketCount = BrokerShellHelperDefaults.SyntheticBinaryDefaultPacketCount,
    int PacketBytes = BrokerShellHelperDefaults.SyntheticBinaryDefaultPacketBytes,
    bool UseMediaCodecSyntheticSource = false,
    bool UseScreenrecordSource = false,
    int EncodedVideoFrames = BrokerShellHelperDefaults.EncodedSyntheticDefaultFrames,
    int EncodedVideoWidth = BrokerShellHelperDefaults.EncodedSyntheticDefaultWidth,
    int EncodedVideoHeight = BrokerShellHelperDefaults.EncodedSyntheticDefaultHeight,
    int EncodedVideoBitrateBps = BrokerShellHelperDefaults.EncodedSyntheticDefaultBitrateBps,
    int ScreenrecordTimeLimitSeconds = BrokerShellHelperDefaults.ScreenrecordDefaultTimeLimitSeconds,
    string ReceiverHost = BrokerClientService.DefaultHost,
    int ReceiveTimeoutMilliseconds = 20000,
    string? AndroidPlayerRoot = null,
    string? PayloadOutputPath = null)
{
    public BrokerShellHelperBinaryProbeOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(Serial))
        {
            throw new ArgumentException("Device serial is required.", nameof(Serial));
        }
        if (UseMediaCodecSyntheticSource && UseScreenrecordSource)
        {
            throw new ArgumentException("Choose either MediaCodec synthetic or screenrecord source for one binary probe.");
        }

        var root = BrokerShellHelperService.ResolveRustyXrRoot(RustyXrRoot);
        var defaultPacketCount = UseScreenrecordSource
            ? BrokerShellHelperDefaults.SyntheticBinaryMaxPacketCount
            : BrokerShellHelperDefaults.SyntheticBinaryDefaultPacketCount;
        var defaultPacketBytes = UseScreenrecordSource
            ? BrokerShellHelperDefaults.ScreenrecordDefaultPacketBytes
            : BrokerShellHelperDefaults.SyntheticBinaryDefaultPacketBytes;
        var normalizedPacketCount = UseScreenrecordSource && PacketCount == BrokerShellHelperDefaults.SyntheticBinaryDefaultPacketCount
            ? defaultPacketCount
            : PacketCount;
        var normalizedPacketBytes = UseScreenrecordSource && PacketBytes == BrokerShellHelperDefaults.SyntheticBinaryDefaultPacketBytes
            ? defaultPacketBytes
            : PacketBytes;
        return this with
        {
            RustyXrRoot = root,
            HelperJarPath = string.IsNullOrWhiteSpace(HelperJarPath)
                ? BrokerShellHelperService.DefaultHelperJarPath(root)
                : Path.GetFullPath(HelperJarPath),
            DeviceJarPath = string.IsNullOrWhiteSpace(DeviceJarPath)
                ? BrokerShellHelperDefaults.DeviceJarPath
                : DeviceJarPath,
            BrokerHost = string.IsNullOrWhiteSpace(BrokerHost)
                ? BrokerClientService.DefaultHost
                : BrokerHost.Trim(),
            BrokerPort = BrokerPort is > 0 and <= 65535 ? BrokerPort : BrokerClientService.DefaultPort,
            CameraOpenId = (CameraOpenId ?? string.Empty).Trim(),
            HostPort = HostPort is > 0 and <= 65535 ? HostPort : BrokerShellHelperDefaults.SyntheticBinaryHostPort,
            DevicePort = DevicePort is > 0 and <= 65535 ? DevicePort : BrokerShellHelperDefaults.SyntheticBinaryDevicePort,
            PacketCount = normalizedPacketCount is > 0 and <= BrokerShellHelperDefaults.SyntheticBinaryMaxPacketCount
                ? normalizedPacketCount
                : defaultPacketCount,
            PacketBytes = normalizedPacketBytes is > 0 and <= BrokerShellHelperDefaults.SyntheticBinaryMaxPacketBytes
                ? normalizedPacketBytes
                : defaultPacketBytes,
            EncodedVideoFrames = EncodedVideoFrames is > 0 and <= BrokerShellHelperDefaults.EncodedSyntheticMaxFrames
                ? EncodedVideoFrames
                : BrokerShellHelperDefaults.EncodedSyntheticDefaultFrames,
            EncodedVideoWidth = EncodedVideoWidth is >= 16 and <= 4096
                ? EncodedVideoWidth
                : BrokerShellHelperDefaults.EncodedSyntheticDefaultWidth,
            EncodedVideoHeight = EncodedVideoHeight is >= 16 and <= 4096
                ? EncodedVideoHeight
                : BrokerShellHelperDefaults.EncodedSyntheticDefaultHeight,
            EncodedVideoBitrateBps = EncodedVideoBitrateBps is >= 1000 and <= 100_000_000
                ? EncodedVideoBitrateBps
                : BrokerShellHelperDefaults.EncodedSyntheticDefaultBitrateBps,
            ScreenrecordTimeLimitSeconds = ScreenrecordTimeLimitSeconds is > 0 and <= BrokerShellHelperDefaults.ScreenrecordMaxTimeLimitSeconds
                ? ScreenrecordTimeLimitSeconds
                : BrokerShellHelperDefaults.ScreenrecordDefaultTimeLimitSeconds,
            ReceiverHost = string.IsNullOrWhiteSpace(ReceiverHost)
                ? BrokerClientService.DefaultHost
                : ReceiverHost.Trim(),
            ReceiveTimeoutMilliseconds = ReceiveTimeoutMilliseconds > 0 ? ReceiveTimeoutMilliseconds : 20000,
            PayloadOutputPath = string.IsNullOrWhiteSpace(PayloadOutputPath)
                ? null
                : Path.GetFullPath(PayloadOutputPath)
        };
    }
}

public sealed record BrokerShellHelperRunResult(
    BrokerShellHelperRunOptions Options,
    CommandResult? BuildResult,
    CommandResult? PushResult,
    CommandResult? LaunchResult,
    DateTimeOffset CompletedAt)
{
    public bool Succeeded =>
        (BuildResult is null || BuildResult.Succeeded) &&
        PushResult is not null &&
        PushResult.Succeeded &&
        LaunchResult is not null &&
        LaunchResult.Succeeded;
}

public sealed record BrokerShellHelperBinaryProbeResult(
    BrokerShellHelperBinaryProbeOptions Options,
    CommandResult? BuildResult,
    CommandResult? PushResult,
    CommandResult? ForwardResult,
    CommandResult? LaunchResult,
    BrokerShellHelperBinaryStreamReport? Stream,
    string Error,
    DateTimeOffset CompletedAt)
{
    public bool Succeeded =>
        (BuildResult is null || BuildResult.Succeeded) &&
        PushResult is not null &&
        PushResult.Succeeded &&
        ForwardResult is not null &&
        ForwardResult.Succeeded &&
        LaunchResult is not null &&
        LaunchResult.Succeeded &&
        Stream is not null &&
        string.IsNullOrWhiteSpace(Error);
}

public sealed record BrokerShellHelperBinaryStreamReport(
    DateTimeOffset CapturedAt,
    string Magic,
    int SchemaVersion,
    int CodecId,
    string Codec,
    int Width,
    int Height,
    int PacketCount,
    int DeclaredPacketBytes,
    long TotalPayloadBytes,
    long TotalWireBytes,
    int HostConnectAttempts,
    long HostConnectElapsedMilliseconds,
    long HostReadDurationMilliseconds,
    long HostReceiveDurationMilliseconds,
    H264NalUnitSummary H264NalUnits,
    BrokerShellHelperPayloadArtifact? PayloadArtifact,
    IReadOnlyList<BrokerShellHelperBinaryPacketReport> Packets);

public sealed record BrokerShellHelperPayloadArtifact(
    string Path,
    long ByteCount,
    string Sha256);

public sealed record H264NalUnitSummary(
    int AnnexBStartCodeCount,
    int NalUnitCount,
    int SpsCount,
    int PpsCount,
    int IdrSliceCount,
    int NonIdrSliceCount,
    int SeiCount,
    int AudCount);

public sealed record BrokerShellHelperBinaryPacketReport(
    int Index,
    long PtsUs,
    int Flags,
    long SourceElapsedNs,
    long SourceUnixNs,
    int SizeBytes,
    uint PayloadChecksum,
    byte FirstByte,
    byte LastByte);
