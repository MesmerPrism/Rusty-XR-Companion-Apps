using System.Text;
using RustyXr.Companion.Core;

namespace RustyXr.Companion.Core.Tests;

public sealed class LslDiagnosticsServiceTests
{
    [Fact]
    public void LocalSummaryCountsMatchedSamplesAndAveragesTiming()
    {
        var samples = new[]
        {
            new LslLocalRoundTripSample(1, 1_000, 10, 0, true, 10, 11, 2_000, 1.0, 0.001, 0.2, 0.05),
            new LslLocalRoundTripSample(2, 2_000, 20, 1, false, null, 21, 5_000, null, 0.003, 0.2, 0.07)
        };

        var summary = LslRoundTripSummary.FromLocal(samples);

        Assert.Equal(2, summary.SampleCount);
        Assert.Equal(1, summary.MatchedSamples);
        Assert.Equal(0.002, summary.MeanHostRoundTripMs!.Value, precision: 6);
        Assert.Equal(1.0, summary.MeanLslReceiveDelayMs!.Value, precision: 6);
        Assert.Equal(0.06, summary.MeanTimeCorrectionUncertaintyMs!.Value, precision: 6);
    }

    [Fact]
    public void BrokerSummaryIncludesBrokerProcessing()
    {
        var samples = new[]
        {
            new LslBrokerRoundTripSample(1, 1_000, null, true, 10, 11, 2_000, 1_100, 1_300, 1.1, 0.001, 0.0001, 0.0002, 0.2, 0.05, null, "{}", 2),
            new LslBrokerRoundTripSample(2, 2_000, null, true, 20, 21, 4_000, 2_100, 2_500, 1.2, 0.002, 0.0001, 0.0004, 0.2, 0.07, null, "{}", 2)
        };

        var summary = LslRoundTripSummary.FromBroker(samples);

        Assert.Equal(2, summary.MatchedSamples);
        Assert.Equal(0.0003, summary.MeanBrokerProcessingMs!.Value, precision: 6);
    }

    [Fact]
    public void PdfRendererCreatesLocalReportPdf()
    {
        var root = Path.Combine(Path.GetTempPath(), "rustyxr-lsl-pdf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var outputPath = Path.Combine(root, "lsl-local-roundtrip.pdf");
        var report = new LslLocalRoundTripReport(
            DateTimeOffset.UtcNow,
            new LslLocalRoundTripOptions(Count: 1, StreamName: "test_stream", SourceId: "source"),
            new LslRuntimeState(true, "test runtime"),
            new LslTimeCorrectionSample(0.001, 10, 0.0002),
            [
                new LslLocalRoundTripSample(1, 1_000, 10, 1, true, 10, 11, 2_000, 1.0, 0.001, 1.0, 0.2)
            ],
            new LslRoundTripSummary(1, 1, 0.001, 0.001, 0.001, 1.0, 0.2, null),
            []);

        LslDiagnosticsPdfRenderer.RenderLocal(report, outputPath);

        var bytes = File.ReadAllBytes(outputPath);
        Assert.True(bytes.Length > 1024);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5), StringComparison.Ordinal);
    }
}
