using System.Security.Cryptography;

namespace RustyXr.Companion.Core;

public sealed class RawLumaArtifactInspectionService
{
    private const int MaxFrameBytes = 64 * 1024 * 1024;

    public async Task<RawLumaArtifactInspectionReport> InspectAsync(
        RawLumaArtifactInspectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var normalized = options.Normalize();
        var frameSizeBytes = checked((long)normalized.Width * normalized.Height);
        if (frameSizeBytes > MaxFrameBytes)
        {
            throw new ArgumentException(
                $"Raw luma frame size {frameSizeBytes} exceeds the supported inspector limit {MaxFrameBytes}.",
                nameof(options));
        }

        var fileInfo = new FileInfo(normalized.PayloadPath);
        var completeFrameCount = fileInfo.Length / frameSizeBytes;
        var trailingBytes = fileInfo.Length % frameSizeBytes;
        var framesToInspect = Math.Min(completeFrameCount, normalized.MaxContactSheetFrames);
        var frameBuffer = new byte[(int)frameSizeBytes];
        var frames = new List<RawLumaFrameInspection>((int)framesToInspect);
        RawLumaContactSheetReport? contactSheet = null;

        await using var payload = new FileStream(
            normalized.PayloadPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var payloadHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var contactSheetStream = CreateContactSheetStream(normalized, framesToInspect, out var contactSheetPath);
        if (contactSheetStream is not null)
        {
            var header = System.Text.Encoding.ASCII.GetBytes(
                $"P5\n{normalized.Width} {normalized.Height * framesToInspect}\n255\n");
            await contactSheetStream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        }

        for (long index = 0; index < completeFrameCount; index++)
        {
            await ReadExactlyAsync(payload, frameBuffer, cancellationToken).ConfigureAwait(false);
            payloadHash.AppendData(frameBuffer);
            if (index < framesToInspect)
            {
                frames.Add(InspectFrame(index, index * frameSizeBytes, frameBuffer));
                if (contactSheetStream is not null)
                {
                    await contactSheetStream.WriteAsync(frameBuffer, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        if (trailingBytes > 0)
        {
            var trailing = new byte[(int)trailingBytes];
            await ReadExactlyAsync(payload, trailing, cancellationToken).ConfigureAwait(false);
            payloadHash.AppendData(trailing);
        }

        if (contactSheetStream is not null && contactSheetPath is not null)
        {
            await contactSheetStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            contactSheet = new RawLumaContactSheetReport(
                contactSheetPath,
                normalized.Width,
                normalized.Height * framesToInspect,
                framesToInspect,
                new FileInfo(contactSheetPath).Length);
        }

        return new RawLumaArtifactInspectionReport(
            DateTimeOffset.UtcNow,
            normalized.PayloadPath,
            "raw_luma8",
            normalized.Width,
            normalized.Height,
            frameSizeBytes,
            fileInfo.Length,
            completeFrameCount,
            trailingBytes,
            Convert.ToHexString(payloadHash.GetHashAndReset()).ToLowerInvariant(),
            frames,
            contactSheet);
    }

    private static FileStream? CreateContactSheetStream(
        RawLumaArtifactInspectionOptions normalized,
        long framesToInspect,
        out string? contactSheetPath)
    {
        contactSheetPath = null;
        if (string.IsNullOrWhiteSpace(normalized.ContactSheetPath) || framesToInspect <= 0)
        {
            return null;
        }

        contactSheetPath = Path.GetFullPath(normalized.ContactSheetPath);
        var directory = Path.GetDirectoryName(contactSheetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new FileStream(contactSheetPath, FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    private static async Task ReadExactlyAsync(
        FileStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of raw luma artifact.");
            }

            offset += read;
        }
    }

    private static RawLumaFrameInspection InspectFrame(long index, long offsetBytes, ReadOnlySpan<byte> frame)
    {
        long checksum = 0L;
        long sum = 0L;
        byte min = byte.MaxValue;
        byte max = byte.MinValue;
        foreach (var value in frame)
        {
            checksum += value;
            sum += value;
            if (value < min)
            {
                min = value;
            }

            if (value > max)
            {
                max = value;
            }
        }

        return new RawLumaFrameInspection(
            index,
            offsetBytes,
            frame.Length,
            checksum,
            frame[0],
            frame[^1],
            min,
            max,
            frame.Length > 0 ? (double)sum / frame.Length : 0.0);
    }
}

public sealed record RawLumaArtifactInspectionOptions(
    string PayloadPath,
    int Width,
    int Height,
    string? ContactSheetPath = null,
    int MaxContactSheetFrames = 8)
{
    public RawLumaArtifactInspectionOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(PayloadPath))
        {
            throw new ArgumentException("Payload path is required.", nameof(PayloadPath));
        }

        if (Width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), "Width must be positive.");
        }

        if (Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Height), "Height must be positive.");
        }

        var fullPath = Path.GetFullPath(PayloadPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Raw luma payload artifact was not found.", fullPath);
        }

        return this with
        {
            PayloadPath = fullPath,
            ContactSheetPath = string.IsNullOrWhiteSpace(ContactSheetPath)
                ? null
                : Path.GetFullPath(ContactSheetPath),
            MaxContactSheetFrames = MaxContactSheetFrames is > 0 and <= 64
                ? MaxContactSheetFrames
                : 8
        };
    }
}

public sealed record RawLumaArtifactInspectionReport(
    DateTimeOffset CapturedAt,
    string PayloadPath,
    string Codec,
    int Width,
    int Height,
    long FrameSizeBytes,
    long ByteCount,
    long CompleteFrameCount,
    long TrailingBytes,
    string Sha256,
    IReadOnlyList<RawLumaFrameInspection> Frames,
    RawLumaContactSheetReport? ContactSheet)
{
    public bool HasCompleteFrames => CompleteFrameCount > 0;
    public bool IsFrameAligned => TrailingBytes == 0;
}

public sealed record RawLumaFrameInspection(
    long Index,
    long OffsetBytes,
    int SizeBytes,
    long PayloadChecksum,
    byte FirstByte,
    byte LastByte,
    byte MinByte,
    byte MaxByte,
    double MeanByte);

public sealed record RawLumaContactSheetReport(
    string Path,
    int Width,
    long Height,
    long FrameCount,
    long ByteCount);
