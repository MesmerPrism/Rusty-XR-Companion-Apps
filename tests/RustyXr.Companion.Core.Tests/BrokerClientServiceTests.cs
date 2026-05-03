using System.Text.Json;
using System.Text.Json.Nodes;
using RustyXr.Companion.Core;

namespace RustyXr.Companion.Core.Tests;

public sealed class BrokerClientServiceTests
{
    [Fact]
    public void BrokerUrisUseForwardedLocalDefaults()
    {
        Assert.Equal(
            "http://127.0.0.1:8765/status",
            BrokerClientService.CreateStatusUri(null).ToString());
        Assert.Equal(
            "ws://127.0.0.1:8765/rustyxr/v1/events",
            BrokerClientService.CreateEventsUri(null).ToString());
        Assert.Equal(
            "ws://localhost:9001/custom",
            BrokerClientService.CreateEventsUri("http://localhost:9001/custom").ToString());
    }

    [Fact]
    public void BrokerCommandPayloadMatchesCommandEnvelope()
    {
        var payload = BrokerClientService.BuildCommandPayload(new BrokerCommandRequest(
            "subscribe",
            "req-1",
            "test-client",
            "Test Client",
            "1.0",
            "latency:sample"));

        Assert.Equal("command", payload.GetProperty("type").GetString());
        Assert.Equal(BrokerClientService.CommandSchema, payload.GetProperty("schema").GetString());
        Assert.Equal("req-1", payload.GetProperty("request_id").GetString());
        Assert.Equal("subscribe", payload.GetProperty("command").GetString());
        Assert.Equal("test-client", payload.GetProperty("client_id").GetString());
        Assert.Equal("latency:sample", payload.GetProperty("params").GetProperty("stream").GetString());
    }

    [Fact]
    public void BrokerCommandPayloadCarriesGenericParameters()
    {
        var payload = BrokerClientService.BuildCommandPayload(new BrokerCommandRequest(
            "configure_osc_ingress",
            "req-osc",
            "test-client",
            "Test Client",
            "1.0",
            Parameters: new JsonObject
            {
                ["enabled"] = true,
                ["port"] = 9000,
                ["address"] = "/rusty-xr/drive/radius"
            }));

        var parameters = payload.GetProperty("params");
        Assert.True(parameters.GetProperty("enabled").GetBoolean());
        Assert.Equal(9000, parameters.GetProperty("port").GetInt32());
        Assert.Equal("/rusty-xr/drive/radius", parameters.GetProperty("address").GetString());
    }

    [Fact]
    public void BrokerLatencySamplePayloadCarriesProbeMetadata()
    {
        var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(1234);
        var payload = BrokerClientService.BuildLatencySamplePayload(new BrokerLatencySampleRequest(
            42,
            "companion_probe",
            256,
            "test-client",
            "Test Client",
            "1.0"), observedAt);

        Assert.Equal("latency_sample", payload.GetProperty("type").GetString());
        Assert.Equal(BrokerClientService.LatencySampleSchema, payload.GetProperty("schema").GetString());
        Assert.Equal(42, payload.GetProperty("sequence_id").GetInt64());
        Assert.Equal("companion_probe", payload.GetProperty("path").GetString());
        Assert.Equal(256, payload.GetProperty("payload_size_bytes").GetInt32());
        Assert.Equal(1_234_000_000L, payload.GetProperty("client_send_time_unix_ns").GetInt64());
    }

    [Fact]
    public async Task QuestAdbServiceForwardsTcp()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"rusty-xr-adb-{Guid.NewGuid():N}");
        var adbPath = Path.Combine(tempRoot, "adb.exe");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(adbPath, string.Empty);
        var previousAdb = Environment.GetEnvironmentVariable("RUSTY_XR_ADB");
        Environment.SetEnvironmentVariable("RUSTY_XR_ADB", adbPath);

        try
        {
            var runner = new RecordingCommandRunner();
            var service = new QuestAdbService(new ToolLocator(runner), runner);

            var result = await service.ForwardTcpAsync("SERIAL", 18765, BrokerClientService.DefaultPort);

            Assert.True(result.Succeeded);
            Assert.Equal(adbPath, result.FileName);
            Assert.Contains("-s SERIAL forward tcp:18765 tcp:8765", result.Arguments);
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
}
