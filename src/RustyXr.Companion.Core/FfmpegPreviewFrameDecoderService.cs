using System.Security.Cryptography;

namespace RustyXr.Companion.Core;

public sealed class FfmpegPreviewFrameDecoderService
{
    public const int DefaultTimeoutMilliseconds = 10_000;

    private readonly ICommandRunner _runner;
    private readonly ToolLocator _toolLocator;
    private readonly Func<string?> _ffmpegResolver;

    public FfmpegPreviewFrameDecoderService(
        ICommandRunner? runner = null,
        ToolLocator? toolLocator = null,
        Func<string?>? ffmpegResolver = null)
    {
        _runner = runner ?? new CommandRunner();
        _toolLocator = toolLocator ?? new ToolLocator(_runner);
        _ffmpegResolver = ffmpegResolver ?? _toolLocator.FindFfmpeg;
    }

    public async Task<FfmpegPreviewFrameDecodeReport> DecodeAsync(
        FfmpegPreviewFrameDecodeOptions options,
        CancellationToken cancellationToken = default)
    {
        var resolvedFfmpegPath = _ffmpegResolver();
        var normalized = options.Normalize(resolvedFfmpegPath);
        CommandResult? command = null;
        var error = string.Empty;
        long outputByteCount = 0;
        var outputSha256 = string.Empty;

        try
        {
            if (string.IsNullOrWhiteSpace(options.FfmpegPath) &&
                string.IsNullOrWhiteSpace(resolvedFfmpegPath))
            {
                error = "FFmpeg executable was not found. Set an FFmpeg path or install the managed media runtime.";
                return new FfmpegPreviewFrameDecodeReport(
                    DateTimeOffset.UtcNow,
                    normalized.PayloadPath,
                    normalized.OutputPath,
                    normalized.Codec,
                    normalized.FrameNumber,
                    normalized.FfmpegPath,
                    null,
                    outputByteCount,
                    outputSha256,
                    error);
            }

            if (File.Exists(normalized.OutputPath) && normalized.OverwriteOutput)
            {
                File.Delete(normalized.OutputPath);
            }

            command = await _runner
                .RunAsync(
                    normalized.FfmpegPath,
                    BuildDecodeArguments(normalized),
                    TimeSpan.FromMilliseconds(normalized.TimeoutMilliseconds),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!command.Succeeded)
            {
                error = command.CondensedOutput;
            }
            else if (!File.Exists(normalized.OutputPath))
            {
                error = "FFmpeg completed without creating a preview frame.";
            }
            else
            {
                var outputInfo = new FileInfo(normalized.OutputPath);
                outputByteCount = outputInfo.Length;
                outputSha256 = await ComputeSha256Async(normalized.OutputPath, cancellationToken).ConfigureAwait(false);
                if (outputByteCount == 0)
                {
                    error = "FFmpeg created an empty preview frame.";
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            error = exception.Message;
        }

        return new FfmpegPreviewFrameDecodeReport(
            DateTimeOffset.UtcNow,
            normalized.PayloadPath,
            normalized.OutputPath,
            normalized.Codec,
            normalized.FrameNumber,
            normalized.FfmpegPath,
            command,
            outputByteCount,
            outputSha256,
            error);
    }

    public static string BuildDecodeArguments(FfmpegPreviewFrameDecodeOptions options)
    {
        var normalized = options.Normalize();
        var overwrite = normalized.OverwriteOutput ? "-y" : "-n";
        return string.Join(
            " ",
            "-nostdin",
            "-hide_banner",
            "-loglevel error",
            overwrite,
            "-f h264",
            "-i",
            QuoteProcessArgument(normalized.PayloadPath),
            "-frames:v",
            normalized.FrameNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            QuoteProcessArgument(normalized.OutputPath));
    }

    private static string QuoteProcessArgument(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record FfmpegPreviewFrameDecodeOptions(
    string PayloadPath,
    string OutputPath,
    string Codec = "h264",
    int FrameNumber = 1,
    string FfmpegPath = "",
    int TimeoutMilliseconds = FfmpegPreviewFrameDecoderService.DefaultTimeoutMilliseconds,
    bool OverwriteOutput = true)
{
    public FfmpegPreviewFrameDecodeOptions Normalize(string? resolvedFfmpegPath = null)
    {
        if (string.IsNullOrWhiteSpace(PayloadPath))
        {
            throw new ArgumentException("Payload path is required.", nameof(PayloadPath));
        }

        var payloadPath = Path.GetFullPath(PayloadPath);
        if (!File.Exists(payloadPath))
        {
            throw new FileNotFoundException("H.264 payload artifact was not found.", payloadPath);
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(OutputPath));
        }

        var outputPath = Path.GetFullPath(OutputPath);
        if (string.IsNullOrWhiteSpace(Path.GetFileName(outputPath)))
        {
            throw new ArgumentException("Output path must include a file name.", nameof(OutputPath));
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var codec = string.IsNullOrWhiteSpace(Codec) ? "h264" : Codec.Trim().ToLowerInvariant();
        if (codec != "h264")
        {
            throw new ArgumentException("Only H.264 elementary-stream preview decode is currently supported.", nameof(Codec));
        }

        if (FrameNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(FrameNumber), "Frame number must be greater than zero.");
        }

        return this with
        {
            PayloadPath = payloadPath,
            OutputPath = outputPath,
            Codec = codec,
            FfmpegPath = string.IsNullOrWhiteSpace(FfmpegPath)
                ? string.IsNullOrWhiteSpace(resolvedFfmpegPath) ? "ffmpeg" : resolvedFfmpegPath.Trim()
                : FfmpegPath.Trim(),
            TimeoutMilliseconds = TimeoutMilliseconds > 0
                ? TimeoutMilliseconds
                : FfmpegPreviewFrameDecoderService.DefaultTimeoutMilliseconds
        };
    }
}

public sealed record FfmpegPreviewFrameDecodeReport(
    DateTimeOffset CapturedAt,
    string PayloadPath,
    string OutputPath,
    string Codec,
    int FrameNumber,
    string FfmpegPath,
    CommandResult? DecodeCommand,
    long OutputByteCount,
    string OutputSha256,
    string Error)
{
    public bool Succeeded =>
        DecodeCommand?.Succeeded == true &&
        OutputByteCount > 0 &&
        File.Exists(OutputPath) &&
        string.IsNullOrWhiteSpace(Error);
}
