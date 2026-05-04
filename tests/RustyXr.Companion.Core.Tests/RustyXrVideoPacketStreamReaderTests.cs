using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using RustyXr.Companion.Core;

namespace RustyXr.Companion.Core.Tests;

public sealed class RustyXrVideoPacketStreamReaderTests
{
    [Fact]
    public void ParseReadsSchema1H264Stream()
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(RustyXrVideoPacketStreamDefaults.Magic));
        WriteU32(stream, 1);
        WriteU32(stream, RustyXrVideoPacketStreamDefaults.CodecH264);
        WriteU32(stream, 1280);
        WriteU32(stream, 720);
        WriteU32(stream, 2);
        WriteU32(stream, 0);
        WritePacket(stream, 0, 2, new byte[] { 0, 0, 0, 1, 0x67, 0x11 });
        WritePacket(stream, 33333, 1, new byte[] { 0, 0, 1, 0x68, 0x22, 0, 0, 1, 0x65, 0x33 });
        stream.Position = 0;

        var report = RustyXrVideoPacketStreamReader.Parse(stream);

        Assert.Equal(RustyXrVideoPacketStreamDefaults.Magic, report.Magic);
        Assert.Equal(1, report.SchemaVersion);
        Assert.Equal("h264", report.Codec);
        Assert.Equal(1280, report.Width);
        Assert.Equal(720, report.Height);
        Assert.Equal(2, report.PacketCount);
        Assert.Equal(16, report.TotalPayloadBytes);
        Assert.Equal(80, report.TotalWireBytes);
        Assert.Equal(3, report.H264NalUnits.NalUnitCount);
        Assert.Equal(1, report.H264NalUnits.SpsCount);
        Assert.Equal(1, report.H264NalUnits.PpsCount);
        Assert.Equal(1, report.H264NalUnits.IdrSliceCount);
        Assert.Equal(2, report.Packets[0].Flags);
        Assert.Equal(33333, report.Packets[1].PtsUs);
    }

    [Fact]
    public void ParseReadsSchema2TimestampsAndWritesPayloadArtifact()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"rusty-xr-video-packets-{Guid.NewGuid():N}");
        var artifactPath = Path.Combine(tempRoot, "stream.h264");
        var payload = new byte[] { 0, 0, 0, 1, 0x67, 0x11, 0, 0, 1, 0x68, 0x22, 0, 0, 1, 0x65, 0x33 };
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(RustyXrVideoPacketStreamDefaults.Magic));
        WriteU32(stream, 2);
        WriteU32(stream, RustyXrVideoPacketStreamDefaults.CodecH264);
        WriteU32(stream, 640);
        WriteU32(stream, 480);
        WriteU32(stream, 1);
        WriteU32(stream, 0);
        WritePacketV2(stream, 33333, 1, 123456789, 1770000000000000000, payload);
        stream.Position = 0;

        try
        {
            var report = RustyXrVideoPacketStreamReader.Parse(stream, artifactPath);

            Assert.Equal(2, report.SchemaVersion);
            Assert.Equal(1, report.PacketCount);
            Assert.Equal(123456789, report.Packets[0].SourceElapsedNs);
            Assert.Equal(1770000000000000000, report.Packets[0].SourceUnixNs);
            Assert.NotNull(report.PayloadArtifact);
            Assert.Equal(Path.GetFullPath(artifactPath), report.PayloadArtifact.Path);
            Assert.Equal(payload.Length, report.PayloadArtifact.ByteCount);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), report.PayloadArtifact.Sha256);
            Assert.Equal(payload, File.ReadAllBytes(artifactPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void WritePacket(Stream stream, long ptsUs, int flags, byte[] payload)
    {
        WriteU64(stream, ptsUs);
        WriteU32(stream, flags);
        WriteU32(stream, payload.Length);
        stream.Write(payload);
    }

    private static void WritePacketV2(
        Stream stream,
        long ptsUs,
        int flags,
        long sourceElapsedNs,
        long sourceUnixNs,
        byte[] payload)
    {
        WriteU64(stream, ptsUs);
        WriteU32(stream, flags);
        WriteU32(stream, payload.Length);
        WriteU64(stream, sourceElapsedNs);
        WriteU64(stream, sourceUnixNs);
        stream.Write(payload);
    }

    private static void WriteU32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteU64(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        stream.Write(buffer);
    }
}
