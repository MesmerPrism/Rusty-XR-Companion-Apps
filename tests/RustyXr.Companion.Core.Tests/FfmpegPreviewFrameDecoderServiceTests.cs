using System.Security.Cryptography;
using RustyXr.Companion.Core;

namespace RustyXr.Companion.Core.Tests;

public sealed class FfmpegPreviewFrameDecoderServiceTests
{
    [Fact]
    public void BuildDecodeArgumentsUsesExternalH264PreviewCommand()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"rusty-xr-ffmpeg-preview-{Guid.NewGuid():N}");
        var payloadPath = Path.Combine(tempRoot, "camera.h264");
        var outputPath = Path.Combine(tempRoot, "frame.png");
        Directory.CreateDirectory(tempRoot);
        File.WriteAllBytes(payloadPath, [0, 0, 1, 0x67, 0, 0, 1, 0x68, 0, 0, 1, 0x65]);

        try
        {
            var arguments = FfmpegPreviewFrameDecoderService.BuildDecodeArguments(
                new FfmpegPreviewFrameDecodeOptions(payloadPath, outputPath, FrameNumber: 1));

            Assert.Contains("-nostdin", arguments);
            Assert.Contains("-f h264", arguments);
            Assert.Contains("-frames:v 1", arguments);
            Assert.Contains(Path.GetFullPath(payloadPath), arguments);
            Assert.Contains(Path.GetFullPath(outputPath), arguments);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DecodeRunsFfmpegAndReportsPreviewArtifact()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"rusty-xr-ffmpeg-preview-{Guid.NewGuid():N}");
        var payloadPath = Path.Combine(tempRoot, "camera.h264");
        var outputPath = Path.Combine(tempRoot, "frame.png");
        var previewBytes = new byte[] { 0x89, 0x50, 0x4e, 0x47, 1, 2, 3, 4 };
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllBytesAsync(payloadPath, [0, 0, 1, 0x67, 0, 0, 1, 0x68, 0, 0, 1, 0x65]);
        var runner = new RecordingCommandRunner(outputPath, previewBytes);

        try
        {
            var report = await new FfmpegPreviewFrameDecoderService(runner)
                .DecodeAsync(new FfmpegPreviewFrameDecodeOptions(
                    payloadPath,
                    outputPath,
                    FfmpegPath: "ffmpeg-test.exe"));

            Assert.True(report.Succeeded);
            Assert.Equal("ffmpeg-test.exe", runner.FileName);
            Assert.Contains("-hide_banner", runner.Arguments);
            Assert.Contains("-loglevel error", runner.Arguments);
            Assert.Contains(Path.GetFullPath(payloadPath), runner.Arguments);
            Assert.Contains(Path.GetFullPath(outputPath), runner.Arguments);
            Assert.Equal(previewBytes.Length, report.OutputByteCount);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(previewBytes)).ToLowerInvariant(),
                report.OutputSha256);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DecodeReportsMissingFfmpegWhenNoPathCanBeResolved()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"rusty-xr-ffmpeg-preview-{Guid.NewGuid():N}");
        var payloadPath = Path.Combine(tempRoot, "camera.h264");
        var outputPath = Path.Combine(tempRoot, "frame.png");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllBytesAsync(payloadPath, [0, 0, 1, 0x67, 0, 0, 1, 0x68, 0, 0, 1, 0x65]);
        var runner = new RecordingCommandRunner(outputPath, [1, 2, 3]);

        try
        {
            var report = await new FfmpegPreviewFrameDecoderService(
                    runner,
                    ffmpegResolver: () => null)
                .DecodeAsync(new FfmpegPreviewFrameDecodeOptions(payloadPath, outputPath));

            Assert.False(report.Succeeded);
            Assert.Null(report.DecodeCommand);
            Assert.Contains("FFmpeg executable was not found", report.Error);
            Assert.Equal(string.Empty, runner.FileName);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class RecordingCommandRunner : ICommandRunner
    {
        private readonly string _outputPath;
        private readonly byte[] _outputBytes;

        public RecordingCommandRunner(string outputPath, byte[] outputBytes)
        {
            _outputPath = outputPath;
            _outputBytes = outputBytes;
        }

        public string FileName { get; private set; } = string.Empty;
        public string Arguments { get; private set; } = string.Empty;

        public async Task<CommandResult> RunAsync(
            string fileName,
            string arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            FileName = fileName;
            Arguments = arguments;
            await File.WriteAllBytesAsync(_outputPath, _outputBytes, cancellationToken);
            return new CommandResult(fileName, arguments, 0, string.Empty, string.Empty, TimeSpan.Zero);
        }
    }
}
