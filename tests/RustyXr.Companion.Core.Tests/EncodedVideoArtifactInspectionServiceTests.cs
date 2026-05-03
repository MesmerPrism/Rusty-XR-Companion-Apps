using System.Security.Cryptography;
using RustyXr.Companion.Core;

namespace RustyXr.Companion.Core.Tests;

public sealed class EncodedVideoArtifactInspectionServiceTests
{
    [Fact]
    public async Task InspectReportsH264StructureAndHash()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"rusty-xr-h264-inspect-{Guid.NewGuid():N}");
        var payloadPath = Path.Combine(tempRoot, "probe.h264");
        var payload = new byte[]
        {
            0, 0, 0, 1, 0x67, 0x11,
            0, 0, 1, 0x68, 0x22,
            0, 0, 1, 0x65, 0x33
        };
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllBytesAsync(payloadPath, payload);

        try
        {
            var report = await new EncodedVideoArtifactInspectionService()
                .InspectAsync(new EncodedVideoArtifactInspectionOptions(payloadPath));

            Assert.Equal(Path.GetFullPath(payloadPath), report.PayloadPath);
            Assert.Equal("h264", report.Codec);
            Assert.Equal(payload.Length, report.ByteCount);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), report.Sha256);
            Assert.Equal(3, report.H264NalUnits.AnnexBStartCodeCount);
            Assert.Equal(1, report.H264NalUnits.SpsCount);
            Assert.Equal(1, report.H264NalUnits.PpsCount);
            Assert.Equal(1, report.H264NalUnits.IdrSliceCount);
            Assert.True(report.HasInspectableH264Structure);
            Assert.False(report.DecoderProbeRequested);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectCanRunOptionalFfmpegDecodeProbe()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"rusty-xr-h264-decode-{Guid.NewGuid():N}");
        var payloadPath = Path.Combine(tempRoot, "probe.h264");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllBytesAsync(payloadPath, new byte[] { 0, 0, 1, 0x67, 0, 0, 1, 0x68, 0, 0, 1, 0x65 });
        var runner = new RecordingCommandRunner();

        try
        {
            var report = await new EncodedVideoArtifactInspectionService(runner)
                .InspectAsync(new EncodedVideoArtifactInspectionOptions(
                    payloadPath,
                    RunDecoderProbe: true,
                    FfmpegPath: "ffmpeg-test.exe"));

            Assert.True(report.DecoderProbeRequested);
            Assert.True(report.DecoderProbeSucceeded);
            Assert.NotNull(report.DecoderProbe);
            Assert.Equal("ffmpeg-test.exe", runner.FileName);
            Assert.Contains("-f h264", runner.Arguments);
            Assert.Contains("-frames:v 1", runner.Arguments);
            Assert.Contains(Path.GetFullPath(payloadPath), runner.Arguments);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class RecordingCommandRunner : ICommandRunner
    {
        public string FileName { get; private set; } = string.Empty;
        public string Arguments { get; private set; } = string.Empty;

        public Task<CommandResult> RunAsync(
            string fileName,
            string arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            FileName = fileName;
            Arguments = arguments;
            return Task.FromResult(new CommandResult(fileName, arguments, 0, string.Empty, string.Empty, TimeSpan.Zero));
        }
    }
}
