using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using RustyXr.Companion.Core;

namespace RustyXr.Companion.Core.Tests;

[Collection("adb-env")]
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
            "http://127.0.0.1:8765/broker/host_manifest",
            BrokerClientService.CreateHostManifestUri(null).ToString());
        Assert.Equal(
            "ws://localhost:9001/custom",
            BrokerClientService.CreateEventsUri("http://localhost:9001/custom").ToString());
        Assert.Equal(
            "http://localhost:9001/custom",
            BrokerClientService.CreateHostManifestUri("http://localhost:9001/custom").ToString());
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
    public void BrokerHostManifestCommandUsesPublicContractName()
    {
        var payload = BrokerClientService.BuildCommandPayload(new BrokerCommandRequest(
            BrokerClientService.HostManifestCommand,
            "req-host",
            "test-client",
            "Test Client",
            "1.0"));

        Assert.Equal(BrokerClientService.CommandSchema, payload.GetProperty("schema").GetString());
        Assert.Equal(BrokerClientService.HostManifestCommand, payload.GetProperty("command").GetString());
    }

    [Fact]
    public void BrokerControlLeaseRequestPayloadUsesPublicContractShape()
    {
        var parameters = BrokerClientService.BuildControlLeaseRequestParameters(
            new BrokerControlLeaseRequest(
                "test-client",
                "runtime.bio",
                "runtime.bio",
                "bio:breath",
                ExpectedRevision: 7,
                RequestedDurationMilliseconds: 60_000,
                OperatorConfirmed: true));
        var payload = BrokerClientService.BuildCommandPayload(new BrokerCommandRequest(
            BrokerClientService.ControlLeaseRequestCommand,
            "lease-req",
            "test-client",
            "Test Client",
            "1.0",
            Parameters: parameters));

        var commandParameters = payload.GetProperty("params");
        Assert.Equal(BrokerClientService.ControlLeaseRequestCommand, payload.GetProperty("command").GetString());
        Assert.Equal(BrokerClientService.ControlLeaseRequestSchema, commandParameters.GetProperty("schema").GetString());
        Assert.Equal("test-client", commandParameters.GetProperty("holder_client_id").GetString());
        Assert.Equal(60_000_000_000L, commandParameters.GetProperty("requested_duration_elapsed_ns").GetInt64());
        Assert.Equal(7, commandParameters.GetProperty("expected_revision").GetInt64());
        Assert.True(commandParameters.GetProperty("operator_confirmed").GetBoolean());
        Assert.Equal("runtime.bio", commandParameters.GetProperty("scope").GetProperty("scope_id").GetString());
        Assert.Equal("bio:breath", commandParameters.GetProperty("scope").GetProperty("resource_id").GetString());
    }

    [Fact]
    public void BrokerControlLeaseReleasePayloadUsesPublicContractShape()
    {
        var parameters = BrokerClientService.BuildControlLeaseReleaseParameters(
            new BrokerControlLeaseRelease(
                "control-lease-1",
                "test-client",
                "runtime.bio",
                "runtime.bio",
                ExpectedRevision: 8,
                Reason: "operator_done"));
        var payload = BrokerClientService.BuildCommandPayload(new BrokerCommandRequest(
            BrokerClientService.ControlLeaseReleaseCommand,
            "lease-release",
            "test-client",
            "Test Client",
            "1.0",
            Parameters: parameters));

        var commandParameters = payload.GetProperty("params");
        Assert.Equal(BrokerClientService.ControlLeaseReleaseCommand, payload.GetProperty("command").GetString());
        Assert.Equal(BrokerClientService.ControlLeaseReleaseSchema, commandParameters.GetProperty("schema").GetString());
        Assert.Equal("control-lease-1", commandParameters.GetProperty("lease_id").GetString());
        Assert.Equal("test-client", commandParameters.GetProperty("holder_client_id").GetString());
        Assert.Equal(8, commandParameters.GetProperty("expected_revision").GetInt64());
        Assert.Equal("operator_done", commandParameters.GetProperty("reason").GetString());
        Assert.Equal("runtime.bio", commandParameters.GetProperty("scope").GetProperty("scope_id").GetString());
    }

    [Fact]
    public async Task BrokerHostManifestProbeReadsHttpEndpoint()
    {
        var handler = new StaticJsonHandler(
            """
            {
              "schema": "rusty.xr.broker.host_manifest.v1",
              "host_id": "synthetic",
              "label": "Synthetic broker host"
            }
            """);
        var client = new BrokerClientService(new HttpClient(handler));

        var result = await client.GetHostManifestAsync(BrokerClientService.CreateHostManifestUri(null));

        Assert.Equal(BrokerClientService.HostManifestPath, handler.RequestUri?.AbsolutePath);
        Assert.Equal(BrokerClientService.HostManifestSchema, result.Manifest.GetProperty("schema").GetString());
        Assert.Equal("synthetic", result.Manifest.GetProperty("host_id").GetString());
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
    public void KioskCommandRunRecordWrapsBrokerStatusEvidence()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "type": "status",
              "rustyKiosk": {
                "schema": "rusty.xr.kiosk.control_plane.v1",
                "phase": "BrokerPanel2d",
                "surface_intent": "RustyKioskDefault",
                "home_mode": "Normal2d",
                "broker_available": true,
                "broker_panel_visible": true,
                "immersive_home_visible": false,
                "shell_helper_connected": false,
                "continuous_adb_shell_required": false,
                "watchdog_required": false,
                "focus_guardian_active": false,
                "proximity_watchdog_active": false,
                "meta_menu_active": false,
                "meta_menu_entry_intentional": false,
                "active_panel": "broker.home",
                "foreground_package": "com.example.rustyxr.broker",
                "foreground_activity": "com.example.rustyxr.broker.MainActivity",
                "clock_epoch_id": "clock.epoch.test",
                "latest_command": null,
                "limitations": []
              }
            }
            """);

        var record = KioskCommandRunRecords.CreateBrokerStatusRecord(
            new Uri("http://127.0.0.1:8765/status"),
            document.RootElement,
            DateTimeOffset.FromUnixTimeMilliseconds(1234),
            "test-run");

        Assert.Equal(KioskCommandRunRecords.CommandRunRecordSchema, record.GetProperty("schema").GetString());
        Assert.Equal("test-run", record.GetProperty("run_id").GetString());
        Assert.Equal("RustyKioskDefault", record.GetProperty("surface_intent").GetString());
        Assert.Equal("Companion", record.GetProperty("primary").GetProperty("provider").GetString());
        Assert.Equal("Broker", record.GetProperty("fallback").GetProperty("provider").GetString());
        Assert.Equal(
            "rusty.xr.kiosk.control_plane.v1",
            record.GetProperty("status_after").GetProperty("schema").GetString());
        Assert.Equal("Succeeded", record.GetProperty("outcome").GetString());
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

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }
}
