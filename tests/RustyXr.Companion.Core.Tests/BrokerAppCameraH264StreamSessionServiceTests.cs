using RustyXr.Companion.Core;

namespace RustyXr.Companion.Core.Tests;

public sealed class BrokerAppCameraH264StreamSessionServiceTests
{
    [Fact]
    public void NormalizeTrimsSessionInputsAndResolvesPayloadPath()
    {
        var payloadPath = Path.Combine(
            Path.GetTempPath(),
            $"rusty-xr-h264-session-{Guid.NewGuid():N}",
            "camera.h264");

        var normalized = new BrokerAppCameraH264StreamSessionOptions(
                " SERIAL ",
                BrokerHost: " ",
                ReceiverHost: " ",
                CameraId: " 50 ",
                LiveStream: true,
                PayloadOutputPath: payloadPath)
            .Normalize();

        Assert.Equal("SERIAL", normalized.Serial);
        Assert.Equal(BrokerClientService.DefaultHost, normalized.BrokerHost);
        Assert.Equal(BrokerClientService.DefaultHost, normalized.ReceiverHost);
        Assert.Equal("50", normalized.CameraId);
        Assert.True(normalized.LiveStream);
        Assert.Equal(Path.GetFullPath(payloadPath), normalized.PayloadOutputPath);
        Assert.StartsWith("app-camera-h264-", normalized.RequestId, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildStartParametersMatchesBrokerStreamContract()
    {
        var parameters = BrokerAppCameraH264StreamSessionService.BuildStartParameters(
            new BrokerAppCameraH264StreamSessionOptions(
                "SERIAL",
                StreamHostPort: 17777,
                StreamDevicePort: 8777,
                CameraId: "50",
                PreferredWidth: 1280,
                PreferredHeight: 720,
                CaptureMilliseconds: 1500,
                MaxPackets: 42,
                BitrateBps: 2_000_000,
                LiveStream: true));

        Assert.Equal(8777, parameters["device_port"]!.GetValue<int>());
        Assert.Equal(17777, parameters["host_port"]!.GetValue<int>());
        Assert.Equal(1280, parameters["preferred_width"]!.GetValue<int>());
        Assert.Equal(720, parameters["preferred_height"]!.GetValue<int>());
        Assert.Equal(1500, parameters["capture_ms"]!.GetValue<int>());
        Assert.Equal(42, parameters["max_packets"]!.GetValue<int>());
        Assert.Equal(2_000_000, parameters["bitrate_bps"]!.GetValue<int>());
        Assert.True(parameters["live_stream"]!.GetValue<bool>());
        Assert.Equal("50", parameters["camera_id"]!.GetValue<string>());
    }

    [Fact]
    public void BuildStartCommandRequestUsesStableCommandMetadata()
    {
        var request = BrokerAppCameraH264StreamSessionService.BuildStartCommandRequest(
            new BrokerAppCameraH264StreamSessionOptions(
                "SERIAL",
                RequestId: "req-123",
                ClientId: "client-123",
                AppLabel: "Session Test",
                AppVersion: "1.2.3"));

        Assert.Equal(BrokerAppCameraH264StreamSessionDefaults.StartCommand, request.Command);
        Assert.Equal("req-123", request.RequestId);
        Assert.Equal("client-123", request.ClientId);
        Assert.Equal("Session Test", request.AppLabel);
        Assert.Equal("1.2.3", request.AppVersion);
        Assert.NotNull(request.Parameters);
    }

    [Fact]
    public void NormalizeRejectsPacketCountsOutsideReaderBounds()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BrokerAppCameraH264StreamSessionOptions(
                    "SERIAL",
                    MaxPackets: RustyXrVideoPacketStreamDefaults.MaxPacketCount + 1)
                .Normalize());

        Assert.Equal("MaxPackets", exception.ParamName);
    }
}
