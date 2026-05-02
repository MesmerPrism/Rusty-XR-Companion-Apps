using RustyXr.Companion.Core;

namespace RustyXr.Companion.Core.Tests;

public sealed class OscServiceTests
{
    [Fact]
    public void OscCodecRoundTripsMessage()
    {
        var message = new OscMessage(
            "/rusty-xr/probe",
            new OscArgument[]
            {
                OscArgument.Int(7),
                OscArgument.Float(0.25f),
                OscArgument.String("hello"),
                OscArgument.Bool(true),
                OscArgument.Bool(false),
                OscArgument.Nil(),
                OscArgument.Impulse(),
                OscArgument.Blob([1, 2, 3])
            });

        var encoded = OscCodec.EncodeMessage(message);
        var decoded = OscCodec.DecodeMessage(encoded);

        Assert.Equal(message.Address, decoded.Address);
        Assert.Equal(message.Arguments.Select(static argument => argument.Kind), decoded.Arguments.Select(static argument => argument.Kind));
        Assert.Equal(7, decoded.Arguments[0].IntValue);
        Assert.Equal(0.25f, decoded.Arguments[1].FloatValue);
        Assert.Equal("hello", decoded.Arguments[2].StringValue);
        Assert.True(decoded.Arguments[3].BoolValue);
        Assert.False(decoded.Arguments[4].BoolValue);
        Assert.Equal(new byte[] { 1, 2, 3 }, decoded.Arguments[7].BlobValue);
    }

    [Fact]
    public void OscCodecPadsStringsByEncodedByteLength()
    {
        var message = new OscMessage(
            "/rusty-xr/probe",
            [OscArgument.String("caf\u00e9"), OscArgument.Int(9)]);

        var decoded = OscCodec.DecodeMessage(OscCodec.EncodeMessage(message));

        Assert.Equal("caf\u00e9", decoded.Arguments[0].StringValue);
        Assert.Equal(9, decoded.Arguments[1].IntValue);
    }

    [Fact]
    public void OscMessageRequiresSlashAddress()
    {
        Assert.True(new OscMessage("/valid", []).IsValid);
        Assert.False(new OscMessage("invalid", []).IsValid);
    }

    [Fact]
    public async Task OscServiceSendsAndReceivesLoopbackMessage()
    {
        var service = new OscService();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var port = ReserveUdpPort();
        var receiveTask = service.ReceiveAsync("127.0.0.1", port, 1, cancellationToken: timeout.Token);
        await Task.Delay(50, timeout.Token);

        await service
            .SendAsync(
                "127.0.0.1",
                port,
                new OscMessage("/rusty-xr/probe", [OscArgument.String("hello")]),
                timeout.Token);

        var result = await receiveTask;

        Assert.Equal(1, result.PacketCount);
        Assert.Equal("/rusty-xr/probe", result.Packets[0].Message.Address);
    }

    private static int ReserveUdpPort()
    {
        using var client = new System.Net.Sockets.UdpClient(0);
        return ((System.Net.IPEndPoint)client.Client.LocalEndPoint!).Port;
    }
}
