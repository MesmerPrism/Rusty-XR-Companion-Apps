using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RustyXr.Companion.Core;

public sealed class BrokerClientService
{
    public const string DefaultHost = "127.0.0.1";
    public const int DefaultPort = 8765;
    public const string EventsPath = "/rustyxr/v1/events";
    public const string StatusPath = "/status";
    public const string CommandSchema = "rusty.xr.broker.command.v1";
    public const string LatencySampleSchema = "rusty.xr.broker.latency_sample.v1";

    private static readonly JsonSerializerOptions BrokerJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly HttpClient _httpClient;

    public BrokerClientService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public static Uri CreateStatusUri(string? explicitUrl, string? host = null, int port = DefaultPort)
    {
        if (!string.IsNullOrWhiteSpace(explicitUrl))
        {
            return new Uri(explicitUrl, UriKind.Absolute);
        }

        ValidatePort(port, nameof(port));
        return new UriBuilder("http", string.IsNullOrWhiteSpace(host) ? DefaultHost : host, port, StatusPath).Uri;
    }

    public static Uri CreateEventsUri(string? explicitUrl, string? host = null, int port = DefaultPort)
    {
        if (!string.IsNullOrWhiteSpace(explicitUrl))
        {
            var uri = new Uri(explicitUrl, UriKind.Absolute);
            return uri.Scheme switch
            {
                "http" => RewriteScheme(uri, "ws"),
                "https" => RewriteScheme(uri, "wss"),
                _ => uri
            };
        }

        ValidatePort(port, nameof(port));
        return new UriBuilder("ws", string.IsNullOrWhiteSpace(host) ? DefaultHost : host, port, EventsPath).Uri;
    }

    public static JsonElement BuildCommandPayload(BrokerCommandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Command))
        {
            throw new ArgumentException("Broker command is required.", nameof(request));
        }

        var root = new JsonObject
        {
            ["type"] = "command",
            ["schema"] = CommandSchema,
            ["request_id"] = string.IsNullOrWhiteSpace(request.RequestId) ? Guid.NewGuid().ToString("N") : request.RequestId,
            ["command"] = request.Command,
            ["client_id"] = string.IsNullOrWhiteSpace(request.ClientId) ? "rusty-xr-companion-cli" : request.ClientId,
            ["app_label"] = string.IsNullOrWhiteSpace(request.AppLabel) ? "Rusty XR Companion CLI" : request.AppLabel,
            ["app_version"] = request.AppVersion ?? string.Empty
        };

        JsonObject? parameters = null;
        if (request.Parameters is not null)
        {
            parameters = JsonNode.Parse(request.Parameters.ToJsonString(BrokerJsonOptions)) as JsonObject;
        }

        if (!string.IsNullOrWhiteSpace(request.Stream))
        {
            parameters ??= [];
            parameters["stream"] = request.Stream;
        }

        if (parameters is not null)
        {
            root["params"] = parameters;
        }

        return ToElement(root);
    }

    public static JsonElement BuildLatencySamplePayload(BrokerLatencySampleRequest request, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            throw new ArgumentException("Latency sample path is required.", nameof(request));
        }

        var observedAt = now ?? DateTimeOffset.UtcNow;
        var root = new JsonObject
        {
            ["type"] = "latency_sample",
            ["schema"] = LatencySampleSchema,
            ["sequence_id"] = request.SequenceId,
            ["path"] = request.Path,
            ["client_send_time_unix_ns"] = UnixTimeNanoseconds(observedAt),
            ["payload_size_bytes"] = Math.Max(0, request.PayloadSizeBytes),
            ["client_id"] = string.IsNullOrWhiteSpace(request.ClientId) ? "rusty-xr-companion-cli" : request.ClientId,
            ["app_label"] = string.IsNullOrWhiteSpace(request.AppLabel) ? "Rusty XR Companion CLI" : request.AppLabel,
            ["app_version"] = request.AppVersion ?? string.Empty
        };

        return ToElement(root);
    }

    public async Task<BrokerStatusProbeResult> GetStatusAsync(
        Uri statusUri,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(statusUri, cancellationToken).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return new BrokerStatusProbeResult(statusUri, ParseElement(raw), DateTimeOffset.Now);
    }

    public Task<BrokerWebSocketProbeResult> SendCommandAsync(
        Uri eventsUri,
        BrokerCommandRequest request,
        TimeSpan listenDuration,
        int maxMessages = 16,
        TimeSpan? replyTimeout = null,
        CancellationToken cancellationToken = default) =>
        SendMessagesAsync(
            eventsUri,
            [new BrokerWebSocketOutboundMessage("command", BuildCommandPayload(request))],
            listenDuration,
            maxMessages,
            replyTimeout,
            cancellationToken);

    public Task<BrokerWebSocketProbeResult> SendLatencySampleAsync(
        Uri eventsUri,
        BrokerLatencySampleRequest request,
        bool subscribeToLatencyStream,
        TimeSpan listenDuration,
        int maxMessages = 16,
        TimeSpan? replyTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<BrokerWebSocketOutboundMessage>();
        if (subscribeToLatencyStream)
        {
            messages.Add(new BrokerWebSocketOutboundMessage(
                "subscribe",
                BuildCommandPayload(new BrokerCommandRequest(
                    "subscribe",
                    $"subscribe-{request.SequenceId}",
                    request.ClientId,
                    request.AppLabel,
                    request.AppVersion,
                    "latency:sample"))));
        }

        messages.Add(new BrokerWebSocketOutboundMessage("latency_sample", BuildLatencySamplePayload(request)));
        return SendMessagesAsync(eventsUri, messages, listenDuration, maxMessages, replyTimeout, cancellationToken);
    }

    public async Task<BrokerWebSocketProbeResult> SendMessagesAsync(
        Uri eventsUri,
        IReadOnlyList<BrokerWebSocketOutboundMessage> messages,
        TimeSpan listenDuration,
        int maxMessages = 16,
        TimeSpan? replyTimeout = null,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
        {
            throw new ArgumentException("At least one broker message is required.", nameof(messages));
        }

        if (maxMessages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessages), "Max messages must be greater than zero.");
        }

        var startedAt = DateTimeOffset.Now;
        var received = new List<BrokerWebSocketReceivedMessage>();
        var timeout = replyTimeout ?? TimeSpan.FromSeconds(5);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(eventsUri, cancellationToken).ConfigureAwait(false);

        var initial = await ReceiveWithTimeoutAsync(socket, timeout, cancellationToken).ConfigureAwait(false);
        if (initial is not null)
        {
            received.Add(new BrokerWebSocketReceivedMessage(ParseElement(initial), DateTimeOffset.Now));
        }

        foreach (var message in messages)
        {
            var text = message.Payload.GetRawText();
            var bytes = Encoding.UTF8.GetBytes(text);
            await socket.SendAsync(
                bytes,
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken).ConfigureAwait(false);

            var reply = await ReceiveWithTimeoutAsync(socket, timeout, cancellationToken).ConfigureAwait(false);
            if (reply is not null)
            {
                received.Add(new BrokerWebSocketReceivedMessage(ParseElement(reply), DateTimeOffset.Now));
            }
        }

        if (listenDuration > TimeSpan.Zero && received.Count < maxMessages)
        {
            using var listenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            listenSource.CancelAfter(listenDuration);
            while (received.Count < maxMessages && socket.State == WebSocketState.Open)
            {
                try
                {
                    var text = await ReceiveTextAsync(socket, listenSource.Token).ConfigureAwait(false);
                    if (text is null)
                    {
                        break;
                    }

                    received.Add(new BrokerWebSocketReceivedMessage(ParseElement(text), DateTimeOffset.Now));
                }
                catch (OperationCanceledException) when (listenSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        if (socket.State == WebSocketState.Open)
        {
            using var closeSource = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "probe complete", closeSource.Token)
                    .ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // The current Quest proof closes the TCP socket without a close reply.
            }
            catch (OperationCanceledException) when (closeSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Probe results are already captured; close timeout is not a probe failure.
            }
        }

        return new BrokerWebSocketProbeResult(eventsUri, messages, received, startedAt, DateTimeOffset.Now);
    }

    private static async Task<string?> ReceiveWithTimeoutAsync(
        ClientWebSocket socket,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            return await ReceiveTextAsync(socket, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        return null;
    }

    private static Uri RewriteScheme(Uri uri, string scheme)
    {
        var builder = new UriBuilder(uri)
        {
            Scheme = scheme,
            Port = uri.IsDefaultPort ? -1 : uri.Port
        };
        return builder.Uri;
    }

    private static JsonElement ToElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString(BrokerJsonOptions));
        return document.RootElement.Clone();
    }

    private static JsonElement ParseElement(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    private static void ValidatePort(int port, string argumentName)
    {
        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(argumentName, "Port must be between 1 and 65535.");
        }
    }

    private static long UnixTimeNanoseconds(DateTimeOffset value) =>
        (value.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks) * 100L;
}

public sealed record BrokerCommandRequest(
    string Command,
    string RequestId,
    string ClientId,
    string AppLabel,
    string? AppVersion,
    string? Stream = null,
    JsonObject? Parameters = null);

public sealed record BrokerLatencySampleRequest(
    long SequenceId,
    string Path,
    int PayloadSizeBytes,
    string ClientId,
    string AppLabel,
    string? AppVersion);

public sealed record BrokerStatusProbeResult(
    Uri Url,
    JsonElement Status,
    DateTimeOffset ReceivedAt);

public sealed record BrokerWebSocketOutboundMessage(
    string Label,
    JsonElement Payload);

public sealed record BrokerWebSocketReceivedMessage(
    JsonElement Payload,
    DateTimeOffset ReceivedAt)
{
    public string Type =>
        Payload.ValueKind == JsonValueKind.Object &&
        Payload.TryGetProperty("type", out var type) &&
        type.ValueKind == JsonValueKind.String
            ? type.GetString() ?? string.Empty
            : string.Empty;
}

public sealed record BrokerWebSocketProbeResult(
    Uri Url,
    IReadOnlyList<BrokerWebSocketOutboundMessage> SentMessages,
    IReadOnlyList<BrokerWebSocketReceivedMessage> ReceivedMessages,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt)
{
    public bool HasAcceptedAck => ReceivedMessages.Any(static message =>
        message.Payload.ValueKind == JsonValueKind.Object &&
        message.Payload.TryGetProperty("type", out var type) &&
        type.ValueKind == JsonValueKind.String &&
        (string.Equals(type.GetString(), "latency_ack", StringComparison.Ordinal) ||
         (string.Equals(type.GetString(), "command_ack", StringComparison.Ordinal) &&
          message.Payload.TryGetProperty("accepted", out var accepted) &&
          accepted.ValueKind == JsonValueKind.True)));
}
