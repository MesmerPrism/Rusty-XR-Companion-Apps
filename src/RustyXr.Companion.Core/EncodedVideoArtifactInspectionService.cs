using System.ComponentModel;
using System.Security.Cryptography;

namespace RustyXr.Companion.Core;

public sealed class EncodedVideoArtifactInspectionService
{
    private const int DefaultBufferSize = 64 * 1024;
    private readonly ICommandRunner _runner;

    public EncodedVideoArtifactInspectionService(ICommandRunner? runner = null)
    {
        _runner = runner ?? new CommandRunner();
    }

    public async Task<EncodedVideoArtifactInspectionReport> InspectAsync(
        EncodedVideoArtifactInspectionOptions options,
        CancellationToken cancellationToken = default)
    {
        var normalized = options.Normalize();
        var summary = new H264NalUnitSummaryBuilder();
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[DefaultBufferSize];
        long byteCount = 0;
        await using (var stream = new FileStream(
                         normalized.PayloadPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         DefaultBufferSize,
                         useAsync: true))
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                var chunk = buffer.AsSpan(0, read);
                summary.Observe(chunk);
                sha256.AppendData(chunk);
                byteCount += read;
            }
        }

        CommandResult? decoderProbe = null;
        var decoderProbeError = string.Empty;
        if (normalized.RunDecoderProbe)
        {
            try
            {
                decoderProbe = await _runner
                    .RunAsync(
                        normalized.FfmpegPath,
                        "-nostdin -hide_banner -v error -f h264 -i " +
                        QuoteProcessArgument(normalized.PayloadPath) +
                        " -frames:v 1 -f null -",
                        TimeSpan.FromMilliseconds(normalized.DecoderProbeTimeoutMilliseconds),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!decoderProbe.Succeeded)
                {
                    decoderProbeError = decoderProbe.CondensedOutput;
                }
            }
            catch (Exception exception) when (exception is Win32Exception or FileNotFoundException)
            {
                decoderProbeError = exception.Message;
            }
        }

        return new EncodedVideoArtifactInspectionReport(
            DateTimeOffset.UtcNow,
            normalized.PayloadPath,
            normalized.Codec,
            byteCount,
            Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant(),
            summary.ToReport(),
            normalized.RunDecoderProbe,
            decoderProbe,
            decoderProbeError);
    }

    private static string QuoteProcessArgument(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

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

public sealed record EncodedVideoArtifactInspectionOptions(
    string PayloadPath,
    string Codec = "h264",
    bool RunDecoderProbe = false,
    string FfmpegPath = "ffmpeg",
    int DecoderProbeTimeoutMilliseconds = 10000)
{
    public EncodedVideoArtifactInspectionOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(PayloadPath))
        {
            throw new ArgumentException("Payload path is required.", nameof(PayloadPath));
        }

        var fullPath = Path.GetFullPath(PayloadPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Encoded video payload artifact was not found.", fullPath);
        }

        var codec = string.IsNullOrWhiteSpace(Codec) ? "h264" : Codec.Trim().ToLowerInvariant();
        if (codec != "h264")
        {
            throw new ArgumentException("Only H.264 elementary-stream artifacts are currently supported.", nameof(Codec));
        }

        return this with
        {
            PayloadPath = fullPath,
            Codec = codec,
            FfmpegPath = string.IsNullOrWhiteSpace(FfmpegPath) ? "ffmpeg" : FfmpegPath.Trim(),
            DecoderProbeTimeoutMilliseconds = DecoderProbeTimeoutMilliseconds > 0
                ? DecoderProbeTimeoutMilliseconds
                : 10000
        };
    }
}

public sealed record EncodedVideoArtifactInspectionReport(
    DateTimeOffset CapturedAt,
    string PayloadPath,
    string Codec,
    long ByteCount,
    string Sha256,
    H264NalUnitSummary H264NalUnits,
    bool DecoderProbeRequested,
    CommandResult? DecoderProbe,
    string DecoderProbeError)
{
    public bool HasH264ParameterSets => H264NalUnits.SpsCount > 0 && H264NalUnits.PpsCount > 0;
    public bool HasH264Slices => H264NalUnits.IdrSliceCount > 0 || H264NalUnits.NonIdrSliceCount > 0;
    public bool HasInspectableH264Structure => HasH264ParameterSets && HasH264Slices;
    public bool DecoderProbeSucceeded =>
        DecoderProbeRequested &&
        DecoderProbe is not null &&
        DecoderProbe.Succeeded &&
        string.IsNullOrWhiteSpace(DecoderProbeError);
}
