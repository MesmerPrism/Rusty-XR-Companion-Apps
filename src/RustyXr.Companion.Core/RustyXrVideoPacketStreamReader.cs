using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace RustyXr.Companion.Core;

public static class RustyXrVideoPacketStreamDefaults
{
    public const string Magic = "RXYRVID1";
    public const int CodecH264 = 1;
    public const int CodecRawLuma8 = 2;
    public const int MaxPacketCount = 720;
    public const int MaxPacketBytes = 1024 * 1024;
}

public static class RustyXrVideoPacketStreamReader
{
    public static Task<RustyXrVideoPacketStreamReport> ReceiveAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return ReceiveAsync(host, port, timeout, payloadOutputPath: null, cancellationToken);
    }

    public static async Task<RustyXrVideoPacketStreamReport> ReceiveAsync(
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
                var report = await ParseAsync(stream, payloadOutputPath, linkedSource.Token).ConfigureAwait(false);
                return report with
                {
                    ConnectAttempts = connectAttempts,
                    ConnectElapsedMilliseconds = connectElapsed,
                    ReceiveDurationMilliseconds = (long)startedAt.Elapsed.TotalMilliseconds
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
            $"Timed out waiting for Rusty XR video packet stream on {host}:{port}.",
            lastError);
    }

    public static RustyXrVideoPacketStreamReport Parse(Stream stream)
    {
        return Parse(stream, payloadOutputPath: null);
    }

    public static RustyXrVideoPacketStreamReport Parse(Stream stream, string? payloadOutputPath)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return ParseCoreAsync(
                length => Task.FromResult(ReadExact(stream, length)),
                DateTimeOffset.UtcNow,
                payloadOutputPath)
            .GetAwaiter()
            .GetResult();
    }

    public static async Task<RustyXrVideoPacketStreamReport> ParseAsync(
        Stream stream,
        string? payloadOutputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return await ParseCoreAsync(
                async length => await ReadExactAsync(stream, length, cancellationToken).ConfigureAwait(false),
                DateTimeOffset.UtcNow,
                payloadOutputPath)
            .ConfigureAwait(false);
    }

    private static async Task<RustyXrVideoPacketStreamReport> ParseCoreAsync(
        Func<int, Task<byte[]>> readExact,
        DateTimeOffset capturedAt,
        string? payloadOutputPath)
    {
        var readStarted = Stopwatch.GetTimestamp();
        using var payloadArtifactWriter = BinaryPayloadArtifactWriter.Create(payloadOutputPath);
        var header = await readExact(32).ConfigureAwait(false);
        var magic = Encoding.ASCII.GetString(header.AsSpan(0, 8));
        if (!string.Equals(magic, RustyXrVideoPacketStreamDefaults.Magic, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unexpected Rusty XR video packet stream magic '{magic}'.");
        }

        var schemaVersion = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8, 4));
        var codecId = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(12, 4));
        var width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        var packetCount = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(24, 4));
        var declaredPacketBytes = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(28, 4));

        if (schemaVersion is not (1 or 2))
        {
            throw new InvalidDataException($"Unsupported Rusty XR video packet stream schema version {schemaVersion}.");
        }
        var codec = codecId switch
        {
            RustyXrVideoPacketStreamDefaults.CodecH264 => "h264",
            RustyXrVideoPacketStreamDefaults.CodecRawLuma8 => "raw_luma8",
            _ => throw new InvalidDataException($"Unsupported Rusty XR video packet stream codec id {codecId}.")
        };
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException($"Invalid Rusty XR video packet stream dimensions {width}x{height}.");
        }
        if (packetCount is <= 0 or > RustyXrVideoPacketStreamDefaults.MaxPacketCount)
        {
            throw new InvalidDataException($"Invalid Rusty XR video packet stream packet count {packetCount}.");
        }
        if (declaredPacketBytes is < 0 or > RustyXrVideoPacketStreamDefaults.MaxPacketBytes)
        {
            throw new InvalidDataException($"Invalid Rusty XR video packet stream packet byte count {declaredPacketBytes}.");
        }

        var packets = new List<RustyXrVideoPacketReport>(packetCount);
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
            if (sizeBytes is <= 0 or > RustyXrVideoPacketStreamDefaults.MaxPacketBytes)
            {
                throw new InvalidDataException($"Invalid Rusty XR video packet stream packet size {sizeBytes} at packet {index}.");
            }
            if (declaredPacketBytes > 0 && sizeBytes != declaredPacketBytes)
            {
                throw new InvalidDataException(
                    $"Rusty XR video packet stream packet {index} size {sizeBytes} did not match declared size {declaredPacketBytes}.");
            }

            var payload = await readExact(sizeBytes).ConfigureAwait(false);
            totalPayloadBytes += payload.Length;
            totalWireBytes += payload.Length;
            if (codecId == RustyXrVideoPacketStreamDefaults.CodecH264)
            {
                h264Summary.Observe(payload);
            }
            payloadArtifactWriter?.Observe(payload);
            packets.Add(new RustyXrVideoPacketReport(
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

        return new RustyXrVideoPacketStreamReport(
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
            ConnectAttempts: 0,
            ConnectElapsedMilliseconds: 0,
            ReadDurationMilliseconds: ElapsedMillisecondsSince(readStarted),
            ReceiveDurationMilliseconds: ElapsedMillisecondsSince(readStarted),
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
                throw new EndOfStreamException("Unexpected EOF while reading Rusty XR video packet stream.");
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
                throw new EndOfStreamException("Unexpected EOF while reading Rusty XR video packet stream.");
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

            var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
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

        public RustyXrVideoPayloadArtifact Complete()
        {
            _stream.Flush(flushToDisk: true);
            _stream.Dispose();
            var sha256 = Convert.ToHexString(_sha256.GetHashAndReset()).ToLowerInvariant();
            File.Move(_temporaryPath, _destinationPath, overwrite: true);
            _completed = true;
            return new RustyXrVideoPayloadArtifact(_destinationPath, _byteCount, sha256);
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
}

public sealed record RustyXrVideoPacketStreamReport(
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
    int ConnectAttempts,
    long ConnectElapsedMilliseconds,
    long ReadDurationMilliseconds,
    long ReceiveDurationMilliseconds,
    H264NalUnitSummary H264NalUnits,
    RustyXrVideoPayloadArtifact? PayloadArtifact,
    IReadOnlyList<RustyXrVideoPacketReport> Packets);

public sealed record RustyXrVideoPayloadArtifact(
    string Path,
    long ByteCount,
    string Sha256);

public sealed record RustyXrVideoPacketReport(
    int Index,
    long PtsUs,
    int Flags,
    long SourceElapsedNs,
    long SourceUnixNs,
    int SizeBytes,
    uint PayloadChecksum,
    byte FirstByte,
    byte LastByte);

public sealed record H264NalUnitSummary(
    int AnnexBStartCodeCount,
    int NalUnitCount,
    int SpsCount,
    int PpsCount,
    int IdrSliceCount,
    int NonIdrSliceCount,
    int SeiCount,
    int AudCount);
