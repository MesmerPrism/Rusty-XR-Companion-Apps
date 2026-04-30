using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace RustyXr.Companion.Core;

public sealed class MediaFrameReceiverService
{
    public const int DefaultPort = 8787;
    private const int MaxHeaderBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public async Task<MediaReceiverResult> ReceiveAsync(
        string host,
        int port,
        string outputDirectory,
        bool once,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            host = IPAddress.Loopback.ToString();
        }

        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");
        }

        Directory.CreateDirectory(outputDirectory);
        var startedAt = DateTimeOffset.Now;
        var frames = new List<MediaFrameRecord>();
        var ledgerPath = Path.Combine(outputDirectory, "frames.jsonl");
        var listener = new TcpListener(IPAddress.Parse(host), port);
        listener.Start();
        try
        {
            do
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                client.NoDelay = true;
                await using var stream = client.GetStream();
                while (!cancellationToken.IsCancellationRequested)
                {
                    var headerSizeBytes = await ReadExactOrNullAsync(stream, 4, cancellationToken).ConfigureAwait(false);
                    if (headerSizeBytes is null)
                    {
                        break;
                    }

                    var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(headerSizeBytes);
                    if (headerSize == 0 || headerSize > MaxHeaderBytes)
                    {
                        throw new InvalidDataException($"Invalid media frame header size: {headerSize}.");
                    }

                    var headerBytes = await ReadExactOrNullAsync(stream, (int)headerSize, cancellationToken).ConfigureAwait(false)
                        ?? throw new EndOfStreamException("Media frame header ended unexpectedly.");
                    var header = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                        Encoding.UTF8.GetString(headerBytes),
                        JsonOptions) ?? new Dictionary<string, JsonElement>();

                    var byteLength = RequiredInt(header, "byte_len");
                    if (byteLength < 0)
                    {
                        throw new InvalidDataException($"Invalid media frame payload size: {byteLength}.");
                    }

                    var payload = await ReadExactOrNullAsync(stream, byteLength, cancellationToken).ConfigureAwait(false)
                        ?? throw new EndOfStreamException("Media frame payload ended unexpectedly.");

                    var frameIndex = OptionalLong(header, "frame_index") ?? frames.Count;
                    var streamName = SafeName(OptionalString(header, "stream") ?? "frame", "frame");
                    var format = SafeName(OptionalString(header, "format") ?? "bin", "bin");
                    var payloadPath = Path.Combine(outputDirectory, $"{streamName}_{frameIndex:00000000}.{ExtensionFor(format)}");
                    await File.WriteAllBytesAsync(payloadPath, payload, cancellationToken).ConfigureAwait(false);

                    var record = new MediaFrameRecord(
                        frameIndex,
                        streamName,
                        format,
                        byteLength,
                        OptionalInt(header, "width"),
                        OptionalInt(header, "height"),
                        OptionalLong(header, "timestamp_ns"),
                        payloadPath,
                        DateTimeOffset.Now);
                    frames.Add(record);
                    await File.AppendAllTextAsync(
                        ledgerPath,
                        JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            while (!once && !cancellationToken.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
        }

        return new MediaReceiverResult(
            host,
            port,
            outputDirectory,
            frames.Count,
            frames,
            startedAt,
            DateTimeOffset.Now);
    }

    private static async Task<byte[]?> ReadExactOrNullAsync(
        NetworkStream stream,
        int byteCount,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[byteCount];
        var offset = 0;
        while (offset < byteCount)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, byteCount - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return offset == 0 ? null : throw new EndOfStreamException("Socket closed mid-frame.");
            }

            offset += read;
        }

        return buffer;
    }

    private static int RequiredInt(IReadOnlyDictionary<string, JsonElement> header, string key)
    {
        if (!header.TryGetValue(key, out var value) || !value.TryGetInt32(out var parsed))
        {
            throw new InvalidDataException($"Media frame header is missing integer field '{key}'.");
        }

        return parsed;
    }

    private static int? OptionalInt(IReadOnlyDictionary<string, JsonElement> header, string key) =>
        header.TryGetValue(key, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;

    private static long? OptionalLong(IReadOnlyDictionary<string, JsonElement> header, string key) =>
        header.TryGetValue(key, out var value) && value.TryGetInt64(out var parsed) ? parsed : null;

    private static string? OptionalString(IReadOnlyDictionary<string, JsonElement> header, string key) =>
        header.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string SafeName(string value, string fallback)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_');
        }

        var safe = builder.ToString().Trim('_');
        return safe.Length == 0 ? fallback : safe;
    }

    private static string ExtensionFor(string format)
    {
        var normalized = format.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized switch
        {
            "png" => "png",
            "jpeg" or "jpg" => "jpg",
            "rgba" or "rgba8888" or "bgra" or "bgra8888" => "rgba",
            "depthu16le" or "u16le" => "u16le",
            _ => "bin"
        };
    }
}
