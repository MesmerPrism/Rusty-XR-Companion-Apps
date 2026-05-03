using RustyXr.Companion.Core;

namespace RustyXr.Companion.Core.Tests;

public sealed class BrokerComparisonServiceTests
{
    [Fact]
    public void DirectOscProbeMessageCarriesReplyPortAndClientSendTime()
    {
        var message = BrokerComparisonService.BuildDirectOscProbeMessage(
            "/rusty-xr/drive/radius",
            0.75f,
            42,
            1_234_567_890L,
            19001);
        var decoded = OscCodec.DecodeMessage(OscCodec.EncodeMessage(message));

        Assert.Equal("/rusty-xr/drive/radius", decoded.Address);
        Assert.Equal(OscArgumentKind.Float, decoded.Arguments[0].Kind);
        Assert.Equal(0.75f, decoded.Arguments[0].FloatValue);
        Assert.Equal(42, decoded.Arguments[1].IntValue);
        Assert.Equal("1234567890", decoded.Arguments[2].StringValue);
        Assert.Equal(19001, decoded.Arguments[3].IntValue);
    }

    [Fact]
    public void DirectOscAcknowledgementParserReadsClockFields()
    {
        var message = new OscMessage(
            "/rusty-xr/drive/ack",
            [
                OscArgument.Int(7),
                OscArgument.String("1000"),
                OscArgument.String("1600"),
                OscArgument.String("1700"),
                OscArgument.Float(0.5f),
                OscArgument.Bool(true)
            ]);

        var parsed = BrokerComparisonService.TryParseDirectOscAcknowledgement(
            message,
            "/rusty-xr/drive/ack",
            out var acknowledgement);

        Assert.True(parsed);
        Assert.Equal(7, acknowledgement.Sequence);
        Assert.Equal(1000, acknowledgement.ClientSendTimeUnixNs);
        Assert.Equal(1600, acknowledgement.TargetReceiveTimeUnixNs);
        Assert.Equal(1700, acknowledgement.TargetAckSendTimeUnixNs);
        Assert.Equal(0.5f, acknowledgement.Value01);
        Assert.True(acknowledgement.AcceptedPulse);
    }

    [Fact]
    public void ClockAlignmentSampleUsesNtpStyleOffsetEstimate()
    {
        var sample = BrokerComparisonClockAlignment.CreateSample(
            "direct_unity_osc_ack",
            1,
            1f,
            acceptedPulse: true,
            hostSendTimeUnixNs: 1_000_000_000L,
            targetReceiveTimeUnixNs: 1_005_000_000L,
            targetSendTimeUnixNs: 1_006_000_000L,
            hostReceiveTimeUnixNs: 1_008_000_000L);

        Assert.Equal(7d, sample.RoundTripMs, precision: 3);
        Assert.Equal(1.5d, sample.TargetMinusHostOffsetMs, precision: 3);
        Assert.Equal(3.5d, sample.HostToTargetAlignedMs, precision: 3);
        Assert.Equal(3.5d, sample.TargetToHostAlignedMs, precision: 3);
    }

    [Fact]
    public void ClockAlignmentSummaryUsesLowestRoundTripQuartileForRecommendedOffset()
    {
        var samples = new[]
        {
            BrokerComparisonClockAlignment.CreateSample("route", 1, 1f, false, 1_000_000_000L, 1_010_000_000L, 1_010_000_000L, 1_020_000_000L),
            BrokerComparisonClockAlignment.CreateSample("route", 2, 1f, false, 1_000_000_000L, 1_006_000_000L, 1_006_000_000L, 1_008_000_000L),
            BrokerComparisonClockAlignment.CreateSample("route", 3, 1f, false, 1_000_000_000L, 1_012_000_000L, 1_012_000_000L, 1_026_000_000L),
            BrokerComparisonClockAlignment.CreateSample("route", 4, 1f, false, 1_000_000_000L, 1_009_000_000L, 1_009_000_000L, 1_014_000_000L)
        };

        var summary = BrokerComparisonClockAlignment.BuildSummary(samples);

        Assert.Equal(4, summary.SampleCount);
        Assert.Equal(2d, summary.RecommendedTargetMinusHostOffsetMs!.Value, precision: 3);
    }
}
