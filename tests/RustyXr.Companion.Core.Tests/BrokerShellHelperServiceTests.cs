using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using RustyXr.Companion.Core;

namespace RustyXr.Companion.Core.Tests;

[Collection("adb-env")]
public sealed class BrokerShellHelperServiceTests
{
    [Fact]
    public void AppProcessCommandUsesShellHelperLaunchContract()
    {
        var command = BrokerShellHelperService.BuildAppProcessShellCommand(
            BrokerShellHelperDefaults.DeviceJarPath,
            "127.0.0.1",
            8765,
            disconnect: false);

        Assert.Contains("CLASSPATH=/data/local/tmp/rusty-xr-broker-shell-helper.jar", command);
        Assert.Contains("app_process / com.example.rustyxr.shell.Helper", command);
        Assert.Contains("--broker-host '127.0.0.1'", command);
        Assert.Contains("--broker-port 8765", command);
        Assert.DoesNotContain("--disconnect", command);
    }

    [Fact]
    public void AppProcessCommandCanReportDisconnect()
    {
        var command = BrokerShellHelperService.BuildAppProcessShellCommand(
            BrokerShellHelperDefaults.DeviceJarPath,
            "127.0.0.1",
            8765,
            disconnect: true);

        Assert.EndsWith("--disconnect", command);
    }

    [Fact]
    public void AppProcessCommandCanRequestCodecProbe()
    {
        var command = BrokerShellHelperService.BuildAppProcessShellCommand(
            BrokerShellHelperDefaults.DeviceJarPath,
            "127.0.0.1",
            8765,
            disconnect: false,
            probeCodecs: true);

        Assert.Contains("--probe-codecs", command);
    }

    [Fact]
    public void AppProcessCommandCanRequestCameraProbe()
    {
        var command = BrokerShellHelperService.BuildAppProcessShellCommand(
            BrokerShellHelperDefaults.DeviceJarPath,
            "127.0.0.1",
            8765,
            disconnect: false,
            probeCameras: true);

        Assert.Contains("--probe-cameras", command);
    }

    [Fact]
    public void AppProcessCommandCanRequestCameraOpenProbe()
    {
        var command = BrokerShellHelperService.BuildAppProcessShellCommand(
            BrokerShellHelperDefaults.DeviceJarPath,
            "127.0.0.1",
            8765,
            disconnect: false,
            probeCameraOpen: true,
            cameraOpenId: "50");

        Assert.Contains("--probe-camera-open", command);
        Assert.Contains("--camera-open-id '50'", command);
    }

    [Fact]
    public void AppProcessCommandCanEmitSyntheticVideoMetadata()
    {
        var command = BrokerShellHelperService.BuildAppProcessShellCommand(
            BrokerShellHelperDefaults.DeviceJarPath,
            "127.0.0.1",
            8765,
            disconnect: false,
            emitSyntheticVideoMetadata: true,
            syntheticVideoSamples: 2);

        Assert.Contains("--emit-synthetic-video-metadata", command);
        Assert.Contains("--synthetic-video-samples 2", command);
    }

    [Fact]
    public void AppProcessCommandCanEmitSyntheticVideoBinary()
    {
        var command = BrokerShellHelperService.BuildAppProcessShellCommand(
            BrokerShellHelperDefaults.DeviceJarPath,
            "127.0.0.1",
            8765,
            disconnect: false,
            emitSyntheticVideoBinary: true,
            syntheticVideoBinaryPort: 8878,
            syntheticVideoPackets: 2,
            syntheticVideoPacketBytes: 256);

        Assert.Contains("--emit-synthetic-video-binary", command);
        Assert.Contains("--binary-video-port 8878", command);
        Assert.Contains("--binary-video-packets 2", command);
        Assert.Contains("--binary-video-packet-bytes 256", command);
    }

    [Fact]
    public void AppProcessCommandCanEmitMediaCodecSyntheticVideo()
    {
        var command = BrokerShellHelperService.BuildAppProcessShellCommand(
            BrokerShellHelperDefaults.DeviceJarPath,
            "127.0.0.1",
            8765,
            disconnect: false,
            emitMediaCodecSyntheticVideo: true,
            syntheticVideoBinaryPort: 8878,
            encodedVideoFrames: 4,
            encodedVideoWidth: 320,
            encodedVideoHeight: 180,
            encodedVideoBitrateBps: 500000);

        Assert.Contains("--emit-mediacodec-synthetic-video", command);
        Assert.Contains("--binary-video-port 8878", command);
        Assert.Contains("--encoded-video-frames 4", command);
        Assert.Contains("--encoded-video-width 320", command);
        Assert.Contains("--encoded-video-height 180", command);
        Assert.Contains("--encoded-video-bitrate 500000", command);
    }

    [Fact]
    public void AppProcessCommandCanEmitScreenrecordVideo()
    {
        var command = BrokerShellHelperService.BuildAppProcessShellCommand(
            BrokerShellHelperDefaults.DeviceJarPath,
            "127.0.0.1",
            8765,
            disconnect: false,
            emitScreenrecordVideo: true,
            syntheticVideoBinaryPort: 8878,
            syntheticVideoPackets: 30,
            syntheticVideoPacketBytes: 16384,
            encodedVideoWidth: 320,
            encodedVideoHeight: 180,
            encodedVideoBitrateBps: 500000,
            screenrecordTimeLimitSeconds: 1);

        Assert.Contains("--emit-screenrecord-video", command);
        Assert.Contains("--binary-video-port 8878", command);
        Assert.Contains("--binary-video-packets 30", command);
        Assert.Contains("--binary-video-packet-bytes 16384", command);
        Assert.Contains("--encoded-video-width 320", command);
        Assert.Contains("--encoded-video-height 180", command);
        Assert.Contains("--encoded-video-bitrate 500000", command);
        Assert.Contains("--screenrecord-time-limit 1", command);
    }

    [Fact]
    public void ScreenrecordBinaryProbeUsesLargerBoundedDefaults()
    {
        var options = new BrokerShellHelperBinaryProbeOptions(
            "SERIAL",
            RustyXrRoot: Path.GetTempPath(),
            UseScreenrecordSource: true).Normalize();

        Assert.Equal(BrokerShellHelperDefaults.SyntheticBinaryMaxPacketCount, options.PacketCount);
        Assert.Equal(BrokerShellHelperDefaults.ScreenrecordDefaultPacketBytes, options.PacketBytes);
    }

    [Fact]
    public void SyntheticBinaryStreamParserValidatesFraming()
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(BrokerShellHelperDefaults.SyntheticBinaryMagic));
        WriteU32(stream, 1);
        WriteU32(stream, 1);
        WriteU32(stream, 1280);
        WriteU32(stream, 720);
        WriteU32(stream, 2);
        WriteU32(stream, 16);
        WritePacket(stream, 0, 1, Enumerable.Range(0, 16).Select(static value => (byte)value).ToArray());
        WritePacket(stream, 33333, 0, Enumerable.Range(1, 16).Select(static value => (byte)value).ToArray());
        stream.Position = 0;

        var report = BrokerShellHelperService.ParseSyntheticBinaryStream(stream);

        Assert.Equal("RXYRVID1", report.Magic);
        Assert.Equal("h264", report.Codec);
        Assert.Equal(1280, report.Width);
        Assert.Equal(720, report.Height);
        Assert.Equal(2, report.PacketCount);
        Assert.Equal(32, report.TotalPayloadBytes);
        Assert.Equal(96, report.TotalWireBytes);
        Assert.Equal(0, report.HostConnectAttempts);
        Assert.True(report.HostReadDurationMilliseconds >= 0);
        Assert.True(report.HostReceiveDurationMilliseconds >= report.HostReadDurationMilliseconds);
        Assert.Equal(120u, report.Packets[0].PayloadChecksum);
        Assert.Equal(136u, report.Packets[1].PayloadChecksum);
        Assert.Equal(33333, report.Packets[1].PtsUs);
    }

    [Fact]
    public void SyntheticBinaryStreamParserReadsSchema2SourceTimestamps()
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(BrokerShellHelperDefaults.SyntheticBinaryMagic));
        WriteU32(stream, 2);
        WriteU32(stream, 1);
        WriteU32(stream, 640);
        WriteU32(stream, 480);
        WriteU32(stream, 1);
        WriteU32(stream, 0);
        WritePacketV2(stream, 33333, 0, 123456789, 1770000000000000000, new byte[] { 1, 2, 3, 4 });
        stream.Position = 0;

        var report = BrokerShellHelperService.ParseSyntheticBinaryStream(stream);

        Assert.Equal(2, report.SchemaVersion);
        Assert.Equal(1, report.PacketCount);
        Assert.Equal(4, report.TotalPayloadBytes);
        Assert.Equal(68, report.TotalWireBytes);
        Assert.Equal(123456789, report.Packets[0].SourceElapsedNs);
        Assert.Equal(1770000000000000000, report.Packets[0].SourceUnixNs);
    }

    [Fact]
    public void SyntheticBinaryStreamParserAllowsVariablePacketSizes()
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(BrokerShellHelperDefaults.SyntheticBinaryMagic));
        WriteU32(stream, 1);
        WriteU32(stream, 1);
        WriteU32(stream, 640);
        WriteU32(stream, 360);
        WriteU32(stream, 2);
        WriteU32(stream, 0);
        WritePacket(stream, 0, 2, new byte[] { 0, 1, 2, 3 });
        WritePacket(stream, 33333, 1, new byte[] { 9, 8, 7 });
        stream.Position = 0;

        var report = BrokerShellHelperService.ParseSyntheticBinaryStream(stream);

        Assert.Equal(0, report.DeclaredPacketBytes);
        Assert.Equal(7, report.TotalPayloadBytes);
        Assert.Equal(71, report.TotalWireBytes);
        Assert.Equal(4, report.Packets[0].SizeBytes);
        Assert.Equal(3, report.Packets[1].SizeBytes);
        Assert.Equal(24u, report.Packets[1].PayloadChecksum);
    }

    [Fact]
    public void SyntheticBinaryStreamParserAcceptsRawLumaCodec()
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(BrokerShellHelperDefaults.SyntheticBinaryMagic));
        WriteU32(stream, 1);
        WriteU32(stream, BrokerShellHelperDefaults.BinaryCodecRawLuma8);
        WriteU32(stream, 4);
        WriteU32(stream, 2);
        WriteU32(stream, 1);
        WriteU32(stream, 8);
        WritePacket(stream, 1234, 0, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        stream.Position = 0;

        var report = BrokerShellHelperService.ParseSyntheticBinaryStream(stream);

        Assert.Equal("raw_luma8", report.Codec);
        Assert.Equal(BrokerShellHelperDefaults.BinaryCodecRawLuma8, report.CodecId);
        Assert.Equal(4, report.Width);
        Assert.Equal(2, report.Height);
        Assert.Equal(8, report.DeclaredPacketBytes);
        Assert.Equal(36u, report.Packets[0].PayloadChecksum);
        Assert.Equal(0, report.H264NalUnits.NalUnitCount);
    }

    [Fact]
    public void SyntheticBinaryStreamParserCanWriteConcatenatedPayloadArtifact()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"rusty-xr-payload-{Guid.NewGuid():N}");
        var artifactPath = Path.Combine(tempRoot, "probe.h264");
        var firstPayload = new byte[] { 0, 0, 0, 1, 0x67, 0x11 };
        var secondPayload = new byte[] { 0, 0, 1, 0x68, 0x22, 0, 0, 1, 0x65, 0x33 };
        var expectedPayload = firstPayload.Concat(secondPayload).ToArray();
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(BrokerShellHelperDefaults.SyntheticBinaryMagic));
        WriteU32(stream, 1);
        WriteU32(stream, 1);
        WriteU32(stream, 320);
        WriteU32(stream, 180);
        WriteU32(stream, 2);
        WriteU32(stream, 0);
        WritePacket(stream, 0, 2, firstPayload);
        WritePacket(stream, 33333, 1, secondPayload);
        stream.Position = 0;

        try
        {
            var report = BrokerShellHelperService.ParseSyntheticBinaryStream(stream, artifactPath);

            Assert.Equal(expectedPayload, File.ReadAllBytes(artifactPath));
            Assert.NotNull(report.PayloadArtifact);
            Assert.Equal(Path.GetFullPath(artifactPath), report.PayloadArtifact.Path);
            Assert.Equal(expectedPayload.Length, report.PayloadArtifact.ByteCount);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(expectedPayload)).ToLowerInvariant(),
                report.PayloadArtifact.Sha256);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void SyntheticBinaryStreamParserReportsH264NalSummaryAcrossPacketBoundaries()
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(BrokerShellHelperDefaults.SyntheticBinaryMagic));
        WriteU32(stream, 1);
        WriteU32(stream, 1);
        WriteU32(stream, 320);
        WriteU32(stream, 180);
        WriteU32(stream, 2);
        WriteU32(stream, 0);
        WritePacket(stream, 0, 0, new byte[] { 0, 0 });
        WritePacket(
            stream,
            33333,
            1,
            new byte[] { 1, 0x67, 0x11, 0, 0, 0, 1, 0x68, 0x22, 0, 0, 1, 0x65, 0x33 });
        stream.Position = 0;

        var report = BrokerShellHelperService.ParseSyntheticBinaryStream(stream);

        Assert.Equal(3, report.H264NalUnits.AnnexBStartCodeCount);
        Assert.Equal(3, report.H264NalUnits.NalUnitCount);
        Assert.Equal(1, report.H264NalUnits.SpsCount);
        Assert.Equal(1, report.H264NalUnits.PpsCount);
        Assert.Equal(1, report.H264NalUnits.IdrSliceCount);
    }

    [Fact]
    public async Task QuestAdbServicePushesHelperJarToDataLocalTmp()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"rusty-xr-helper-{Guid.NewGuid():N}");
        var adbPath = Path.Combine(tempRoot, "adb.exe");
        var helperPath = Path.Combine(tempRoot, BrokerShellHelperDefaults.HelperJarFileName);
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(adbPath, string.Empty);
        await File.WriteAllTextAsync(helperPath, "jar placeholder");
        var previousAdb = Environment.GetEnvironmentVariable("RUSTY_XR_ADB");
        Environment.SetEnvironmentVariable("RUSTY_XR_ADB", adbPath);

        try
        {
            var runner = new RecordingCommandRunner();
            var service = new QuestAdbService(new ToolLocator(runner), runner);

            var result = await service.PushFileAsync(
                "SERIAL",
                helperPath,
                BrokerShellHelperDefaults.DeviceJarPath);

            Assert.True(result.Succeeded);
            Assert.Equal(adbPath, result.FileName);
            Assert.Contains("-s SERIAL push", result.Arguments);
            Assert.Contains(BrokerShellHelperDefaults.HelperJarFileName, result.Arguments);
            Assert.Contains(BrokerShellHelperDefaults.DeviceJarPath, result.Arguments);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RUSTY_XR_ADB", previousAdb);
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class RecordingCommandRunner : ICommandRunner
    {
        public Task<CommandResult> RunAsync(
            string fileName,
            string arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CommandResult(fileName, arguments, 0, string.Empty, string.Empty, TimeSpan.Zero));
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
