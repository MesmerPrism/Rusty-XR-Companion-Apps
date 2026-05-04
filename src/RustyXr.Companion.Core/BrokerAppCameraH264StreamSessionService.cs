using System.Text.Json.Nodes;

namespace RustyXr.Companion.Core;

public static class BrokerAppCameraH264StreamSessionDefaults
{
    public const string StartCommand = "camera_provider.start_app_camera_h264_stream";
    public const int StreamHostPort = 18879;
    public const int StreamDevicePort = 8879;
    public const int PreferredWidth = 720;
    public const int PreferredHeight = 480;
    public const int CaptureMilliseconds = 900;
    public const int MaxPackets = 12;
    public const int BitrateBps = 1_000_000;
    public const int ReceiveTimeoutMilliseconds = 30_000;
    public const int BrokerReplyTimeoutMilliseconds = 10_000;
    public const string ClientId = "rusty-xr-companion-cli";
    public const string AppLabel = "Rusty XR Companion CLI";
}

public sealed class BrokerAppCameraH264StreamSessionService
{
    private readonly QuestAdbService _adbService;
    private readonly BrokerClientService _brokerClientService;

    public BrokerAppCameraH264StreamSessionService(
        QuestAdbService? adbService = null,
        BrokerClientService? brokerClientService = null)
    {
        _adbService = adbService ?? new QuestAdbService();
        _brokerClientService = brokerClientService ?? new BrokerClientService();
    }

    public async Task<BrokerAppCameraH264StreamSessionResult> RunAsync(
        BrokerAppCameraH264StreamSessionOptions options,
        CancellationToken cancellationToken = default)
    {
        var normalized = options.Normalize();
        var brokerForward = new CommandResult("adb", "forward", -1, string.Empty, string.Empty, TimeSpan.Zero);
        CommandResult? streamForward = null;
        BrokerWebSocketProbeResult? command = null;
        RustyXrVideoPacketStreamReport? stream = null;
        var error = string.Empty;

        try
        {
            brokerForward = await _adbService
                .ForwardTcpAsync(
                    normalized.Serial,
                    normalized.BrokerHostPort,
                    normalized.BrokerDevicePort,
                    cancellationToken)
                .ConfigureAwait(false);

            if (brokerForward.Succeeded)
            {
                streamForward = await _adbService
                    .ForwardTcpAsync(
                        normalized.Serial,
                        normalized.StreamHostPort,
                        normalized.StreamDevicePort,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!brokerForward.Succeeded)
            {
                error = $"Broker ADB forward failed: {brokerForward.CondensedOutput}";
            }
            else if (streamForward is null || !streamForward.Succeeded)
            {
                error = $"Binary ADB forward failed: {streamForward?.CondensedOutput ?? "not attempted"}";
            }
            else
            {
                command = await _brokerClientService
                    .SendCommandAsync(
                        BrokerClientService.CreateEventsUri(null, normalized.BrokerHost, normalized.BrokerHostPort),
                        BuildStartCommandRequest(normalized),
                        TimeSpan.Zero,
                        maxMessages: 16,
                        replyTimeout: TimeSpan.FromMilliseconds(normalized.BrokerReplyTimeoutMilliseconds),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!command.HasAcceptedAck)
                {
                    error = $"Broker did not accept {BrokerAppCameraH264StreamSessionDefaults.StartCommand}.";
                }
                else
                {
                    stream = await RustyXrVideoPacketStreamReader
                        .ReceiveAsync(
                            normalized.ReceiverHost,
                            normalized.StreamHostPort,
                            TimeSpan.FromMilliseconds(normalized.ReceiveTimeoutMilliseconds),
                            normalized.PayloadOutputPath,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            error = exception.Message;
        }

        return new BrokerAppCameraH264StreamSessionResult(
            DateTimeOffset.Now,
            normalized,
            brokerForward,
            streamForward,
            command,
            stream,
            error);
    }

    public static BrokerCommandRequest BuildStartCommandRequest(BrokerAppCameraH264StreamSessionOptions options)
    {
        var normalized = options.Normalize();
        return new BrokerCommandRequest(
            BrokerAppCameraH264StreamSessionDefaults.StartCommand,
            normalized.RequestId,
            normalized.ClientId,
            normalized.AppLabel,
            normalized.AppVersion,
            Parameters: BuildStartParameters(normalized));
    }

    public static JsonObject BuildStartParameters(BrokerAppCameraH264StreamSessionOptions options)
    {
        var normalized = options.Normalize();
        var parameters = new JsonObject
        {
            ["device_port"] = normalized.StreamDevicePort,
            ["host_port"] = normalized.StreamHostPort,
            ["preferred_width"] = normalized.PreferredWidth,
            ["preferred_height"] = normalized.PreferredHeight,
            ["capture_ms"] = normalized.CaptureMilliseconds,
            ["max_packets"] = normalized.MaxPackets,
            ["bitrate_bps"] = normalized.BitrateBps,
            ["live_stream"] = normalized.LiveStream
        };

        if (!string.IsNullOrWhiteSpace(normalized.CameraId))
        {
            parameters["camera_id"] = normalized.CameraId;
        }

        return parameters;
    }
}

public sealed record BrokerAppCameraH264StreamSessionOptions(
    string Serial,
    string BrokerHost = BrokerClientService.DefaultHost,
    int BrokerHostPort = BrokerClientService.DefaultPort,
    int BrokerDevicePort = BrokerClientService.DefaultPort,
    string ReceiverHost = BrokerClientService.DefaultHost,
    int StreamHostPort = BrokerAppCameraH264StreamSessionDefaults.StreamHostPort,
    int StreamDevicePort = BrokerAppCameraH264StreamSessionDefaults.StreamDevicePort,
    string CameraId = "",
    int PreferredWidth = BrokerAppCameraH264StreamSessionDefaults.PreferredWidth,
    int PreferredHeight = BrokerAppCameraH264StreamSessionDefaults.PreferredHeight,
    int CaptureMilliseconds = BrokerAppCameraH264StreamSessionDefaults.CaptureMilliseconds,
    int MaxPackets = BrokerAppCameraH264StreamSessionDefaults.MaxPackets,
    int BitrateBps = BrokerAppCameraH264StreamSessionDefaults.BitrateBps,
    bool LiveStream = false,
    string RequestId = "",
    string ClientId = BrokerAppCameraH264StreamSessionDefaults.ClientId,
    string AppLabel = BrokerAppCameraH264StreamSessionDefaults.AppLabel,
    string? AppVersion = null,
    string? PayloadOutputPath = null,
    int ReceiveTimeoutMilliseconds = BrokerAppCameraH264StreamSessionDefaults.ReceiveTimeoutMilliseconds,
    int BrokerReplyTimeoutMilliseconds = BrokerAppCameraH264StreamSessionDefaults.BrokerReplyTimeoutMilliseconds)
{
    public BrokerAppCameraH264StreamSessionOptions Normalize()
    {
        var serial = (Serial ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException("Device serial is required.", nameof(Serial));
        }

        var requestId = string.IsNullOrWhiteSpace(RequestId)
            ? $"app-camera-h264-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            : RequestId.Trim();

        return this with
        {
            Serial = serial,
            BrokerHost = NormalizeHost(BrokerHost),
            BrokerHostPort = RequirePort(BrokerHostPort, nameof(BrokerHostPort)),
            BrokerDevicePort = RequirePort(BrokerDevicePort, nameof(BrokerDevicePort)),
            ReceiverHost = NormalizeHost(ReceiverHost),
            StreamHostPort = RequirePort(StreamHostPort, nameof(StreamHostPort)),
            StreamDevicePort = RequirePort(StreamDevicePort, nameof(StreamDevicePort)),
            CameraId = CameraId?.Trim() ?? string.Empty,
            PreferredWidth = RequirePositive(PreferredWidth, nameof(PreferredWidth)),
            PreferredHeight = RequirePositive(PreferredHeight, nameof(PreferredHeight)),
            CaptureMilliseconds = RequirePositive(CaptureMilliseconds, nameof(CaptureMilliseconds)),
            MaxPackets = RequirePacketCount(MaxPackets, nameof(MaxPackets)),
            BitrateBps = RequirePositive(BitrateBps, nameof(BitrateBps)),
            RequestId = requestId,
            ClientId = string.IsNullOrWhiteSpace(ClientId)
                ? BrokerAppCameraH264StreamSessionDefaults.ClientId
                : ClientId.Trim(),
            AppLabel = string.IsNullOrWhiteSpace(AppLabel)
                ? BrokerAppCameraH264StreamSessionDefaults.AppLabel
                : AppLabel.Trim(),
            AppVersion = AppVersion?.Trim(),
            PayloadOutputPath = string.IsNullOrWhiteSpace(PayloadOutputPath)
                ? null
                : Path.GetFullPath(PayloadOutputPath!),
            ReceiveTimeoutMilliseconds = RequirePositive(ReceiveTimeoutMilliseconds, nameof(ReceiveTimeoutMilliseconds)),
            BrokerReplyTimeoutMilliseconds = RequirePositive(BrokerReplyTimeoutMilliseconds, nameof(BrokerReplyTimeoutMilliseconds))
        };
    }

    private static string NormalizeHost(string? host) =>
        string.IsNullOrWhiteSpace(host) ? BrokerClientService.DefaultHost : host.Trim();

    private static int RequirePort(int value, string argumentName)
    {
        if (value is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(argumentName, "Port must be between 1 and 65535.");
        }

        return value;
    }

    private static int RequirePositive(int value, string argumentName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(argumentName, "Value must be greater than zero.");
        }

        return value;
    }

    private static int RequirePacketCount(int value, string argumentName)
    {
        if (value is <= 0 or > RustyXrVideoPacketStreamDefaults.MaxPacketCount)
        {
            throw new ArgumentOutOfRangeException(
                argumentName,
                $"Packet count must be between 1 and {RustyXrVideoPacketStreamDefaults.MaxPacketCount}.");
        }

        return value;
    }
}

public sealed record BrokerAppCameraH264StreamSessionResult(
    DateTimeOffset CapturedAt,
    BrokerAppCameraH264StreamSessionOptions Options,
    CommandResult BrokerForwardResult,
    CommandResult? BinaryForwardResult,
    BrokerWebSocketProbeResult? Command,
    RustyXrVideoPacketStreamReport? Stream,
    string Error)
{
    public bool Succeeded =>
        BrokerForwardResult.Succeeded &&
        (BinaryForwardResult?.Succeeded ?? false) &&
        Command?.HasAcceptedAck == true &&
        Stream is not null &&
        string.Equals(Stream.Codec, "h264", StringComparison.Ordinal) &&
        string.IsNullOrWhiteSpace(Error);
}
