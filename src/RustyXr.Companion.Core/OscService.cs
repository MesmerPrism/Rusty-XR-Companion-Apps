using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RustyXr.Companion.Core;

public sealed class OscService
{
    public const int DefaultPort = 9000;
    public const int DefaultMaxPacketBytes = 8192;

    public async Task<OscSendResult> SendAsync(
        string host,
        int port,
        OscMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!message.IsValid)
        {
            throw new ArgumentException("OSC message requires a slash-prefixed address.", nameof(message));
        }

        ValidatePort(port);
        var bytes = OscCodec.EncodeMessage(message);
        using var client = new UdpClient(AddressFamily.InterNetwork);
        var sent = await client
            .SendAsync(bytes, bytes.Length, host, port)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        return new OscSendResult(host, port, message, sent, DateTimeOffset.Now);
    }

    public async Task<OscReceiveResult> ReceiveAsync(
        string host,
        int port,
        int count,
        int maxPacketBytes = DefaultMaxPacketBytes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            host = IPAddress.Any.ToString();
        }

        ValidatePort(port);
        count = Math.Max(1, count);
        maxPacketBytes = Math.Clamp(maxPacketBytes, 256, 65_507);
        var endpoint = new IPEndPoint(IPAddress.Parse(host), port);
        using var client = new UdpClient(endpoint);
        var startedAt = DateTimeOffset.Now;
        var packets = new List<OscReceivedMessage>();
        while (packets.Count < count && !cancellationToken.IsCancellationRequested)
        {
            var result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (result.Buffer.Length > maxPacketBytes)
            {
                throw new InvalidDataException($"OSC datagram exceeded max packet size: {result.Buffer.Length}.");
            }

            packets.Add(new OscReceivedMessage(
                OscCodec.DecodeMessage(result.Buffer),
                result.RemoteEndPoint.ToString(),
                result.Buffer.Length,
                DateTimeOffset.Now));
        }

        return new OscReceiveResult(host, port, packets.Count, packets, startedAt, DateTimeOffset.Now);
    }

    private static void ValidatePort(int port)
    {
        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");
        }
    }
}

public static class OscCodec
{
    public static byte[] EncodeMessage(OscMessage message)
    {
        if (!message.IsValid)
        {
            throw new ArgumentException("OSC message requires a slash-prefixed address.", nameof(message));
        }

        var bytes = new List<byte>();
        WritePaddedString(bytes, message.Address);
        WritePaddedString(bytes, "," + string.Concat(message.Arguments.Select(static argument => argument.TypeTag)));
        foreach (var argument in message.Arguments)
        {
            switch (argument.Kind)
            {
                case OscArgumentKind.Int:
                    WriteInt(bytes, argument.IntValue ?? 0);
                    break;
                case OscArgumentKind.Float:
                    WriteUInt(bytes, BitConverter.SingleToUInt32Bits(argument.FloatValue ?? 0.0f));
                    break;
                case OscArgumentKind.String:
                    WritePaddedString(bytes, argument.StringValue ?? string.Empty);
                    break;
                case OscArgumentKind.Blob:
                    var blob = argument.BlobValue ?? Array.Empty<byte>();
                    WriteInt(bytes, blob.Length);
                    bytes.AddRange(blob);
                    WritePadding(bytes, blob.Length);
                    break;
                case OscArgumentKind.Bool:
                case OscArgumentKind.Nil:
                case OscArgumentKind.Impulse:
                    break;
                default:
                    throw new InvalidDataException($"Unsupported OSC argument kind: {argument.Kind}.");
            }
        }

        return bytes.ToArray();
    }

    public static OscMessage DecodeMessage(ReadOnlySpan<byte> bytes)
    {
        var offset = 0;
        var address = ReadPaddedString(bytes, ref offset);
        if (!OscMessage.ValidAddress(address))
        {
            throw new InvalidDataException($"Invalid OSC address: {address}");
        }

        var tags = ReadPaddedString(bytes, ref offset);
        if (!tags.StartsWith(",", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Invalid OSC type tag string: {tags}");
        }

        var arguments = new List<OscArgument>();
        foreach (var tag in tags.Skip(1))
        {
            arguments.Add(tag switch
            {
                'i' => OscArgument.Int(ReadInt(bytes, ref offset)),
                'f' => OscArgument.Float(BitConverter.UInt32BitsToSingle(ReadUInt(bytes, ref offset))),
                's' => OscArgument.String(ReadPaddedString(bytes, ref offset)),
                'b' => OscArgument.Blob(ReadBlob(bytes, ref offset)),
                'T' => OscArgument.Bool(true),
                'F' => OscArgument.Bool(false),
                'N' => OscArgument.Nil(),
                'I' => OscArgument.Impulse(),
                _ => throw new InvalidDataException($"Unsupported OSC type tag: {tag}.")
            });
        }

        if (offset != bytes.Length)
        {
            throw new InvalidDataException($"OSC packet has {bytes.Length - offset} trailing byte(s).");
        }

        return new OscMessage(address, arguments);
    }

    private static byte[] ReadBlob(ReadOnlySpan<byte> bytes, ref int offset)
    {
        var length = ReadInt(bytes, ref offset);
        if (length < 0 || offset + length > bytes.Length)
        {
            throw new InvalidDataException($"Invalid OSC blob length: {length}.");
        }

        var blob = bytes.Slice(offset, length).ToArray();
        offset += PaddedLength(length);
        if (offset > bytes.Length)
        {
            throw new InvalidDataException("OSC blob padding exceeded packet length.");
        }

        return blob;
    }

    private static string ReadPaddedString(ReadOnlySpan<byte> bytes, ref int offset)
    {
        if (offset >= bytes.Length)
        {
            throw new EndOfStreamException("OSC string starts beyond packet length.");
        }

        var start = offset;
        while (offset < bytes.Length && bytes[offset] != 0)
        {
            offset++;
        }

        if (offset >= bytes.Length)
        {
            throw new InvalidDataException("OSC string is missing a null terminator.");
        }

        var value = Encoding.UTF8.GetString(bytes.Slice(start, offset - start));
        offset = start + PaddedLength(offset - start + 1);
        if (offset > bytes.Length)
        {
            throw new InvalidDataException("OSC string padding exceeded packet length.");
        }

        return value;
    }

    private static int ReadInt(ReadOnlySpan<byte> bytes, ref int offset)
    {
        if (offset + 4 > bytes.Length)
        {
            throw new EndOfStreamException("OSC int32 exceeded packet length.");
        }

        var value = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(offset, 4));
        offset += 4;
        return value;
    }

    private static uint ReadUInt(ReadOnlySpan<byte> bytes, ref int offset)
    {
        if (offset + 4 > bytes.Length)
        {
            throw new EndOfStreamException("OSC uint32 exceeded packet length.");
        }

        var value = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
        offset += 4;
        return value;
    }

    private static void WritePaddedString(List<byte> bytes, string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value);
        bytes.AddRange(encoded);
        bytes.Add(0);
        WritePadding(bytes, encoded.Length + 1);
    }

    private static void WriteInt(List<byte> bytes, int value)
    {
        Span<byte> data = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(data, value);
        bytes.AddRange(data.ToArray());
    }

    private static void WriteUInt(List<byte> bytes, uint value)
    {
        Span<byte> data = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(data, value);
        bytes.AddRange(data.ToArray());
    }

    private static void WritePadding(List<byte> bytes, int unpaddedLength)
    {
        var padding = (4 - (unpaddedLength % 4)) % 4;
        for (var index = 0; index < padding; index++)
        {
            bytes.Add(0);
        }
    }

    private static int PaddedLength(int length) => length + ((4 - (length % 4)) % 4);
}
