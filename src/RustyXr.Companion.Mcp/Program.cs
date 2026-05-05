using System.Text.Json;
using System.Text.Json.Nodes;
using RustyXr.Companion.Core;

var server = new RustyXrMcpServer(Console.In, Console.Out);
await server.RunAsync().ConfigureAwait(false);

internal sealed class RustyXrMcpServer
{
    private const string ProtocolVersion = "2025-11-25";
    private const string ServerName = "rusty-xr-companion-mcp";
    private const string ServerTitle = "Rusty XR Companion MCP";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly TextReader _input;
    private readonly TextWriter _output;

    public RustyXrMcpServer(TextReader input, TextWriter output)
    {
        _input = input;
        _output = output;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var response = await HandleLineAsync(line, cancellationToken).ConfigureAwait(false);
            if (response is not null)
            {
                await _output.WriteLineAsync(response).ConfigureAwait(false);
                await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<string?> HandleLineAsync(string line, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var request = document.RootElement;
            var id = request.TryGetProperty("id", out var idElement)
                ? idElement.Clone()
                : (JsonElement?)null;

            if (!request.TryGetProperty("method", out var methodElement) ||
                methodElement.ValueKind != JsonValueKind.String)
            {
                return id is null
                    ? null
                    : SerializeResponse(new JsonRpcResponse(id.Value, null, Error(-32600, "Invalid JSON-RPC request.")));
            }

            var method = methodElement.GetString() ?? string.Empty;
            if (id is null)
            {
                return method.StartsWith("notifications/", StringComparison.Ordinal)
                    ? null
                    : null;
            }

            var result = method switch
            {
                "initialize" => Initialize(),
                "ping" => new JsonObject(),
                "tools/list" => ListTools(),
                "tools/call" => await CallToolAsync(
                    request.TryGetProperty("params", out var paramsElement)
                        ? paramsElement
                        : default,
                    cancellationToken).ConfigureAwait(false),
                _ => null
            };

            return result is null
                ? SerializeResponse(new JsonRpcResponse(id.Value, null, Error(-32601, $"Method '{method}' is not supported.")))
                : SerializeResponse(new JsonRpcResponse(id.Value, result, null));
        }
        catch (JsonException exception)
        {
            return SerializeResponse(new JsonRpcResponse(null, null, Error(-32700, exception.Message)));
        }
        catch (ArgumentException exception)
        {
            return SerializeResponse(new JsonRpcResponse(null, null, Error(-32602, exception.Message)));
        }
        catch (Exception exception)
        {
            return SerializeResponse(new JsonRpcResponse(null, null, Error(-32603, exception.Message)));
        }
    }

    private static object Initialize()
    {
        var identity = AppBuildIdentity.Detect();
        return new
        {
            protocolVersion = ProtocolVersion,
            capabilities = new
            {
                tools = new
                {
                    listChanged = false
                }
            },
            serverInfo = new
            {
                name = ServerName,
                title = ServerTitle,
                version = identity.CurrentVersion
            },
            instructions = "Exposes Rusty XR Companion read-only operations and inspectable command plans for side-effecting operations."
        };
    }

    private static object ListTools()
    {
        var toolList = CompanionOperationSurface.ToMcpToolList(CompanionOperationSurface.Create());
        return new
        {
            tools = toolList.Tools.Select(static tool => new
            {
                tool.Name,
                tool.Description,
                tool.InputSchema,
                tool.Annotations
            }).ToArray()
        };
    }

    private static async Task<object> CallToolAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("tools/call requires params.name.");
        }

        var name = nameElement.GetString() ?? string.Empty;
        var toolList = CompanionOperationSurface.ToMcpToolList(CompanionOperationSurface.Create());
        var tool = toolList.Tools.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal));
        if (tool is null)
        {
            throw new ArgumentException($"Unknown tool '{name}'.");
        }

        var arguments = parameters.TryGetProperty("arguments", out var argumentsElement) &&
                        argumentsElement.ValueKind == JsonValueKind.Object
            ? argumentsElement
            : default;
        var inputs = ToInputs(arguments);

        if (tool.Binding.OperationId == "api.plan")
        {
            var targetOperation = Required(inputs, "operation");
            var targetInputs = ParseNestedInputs(arguments);
            var allowSideEffects = BoolInput(inputs, "allowSideEffects");
            var plan = CompanionOperationPlanner.CreatePlan(targetOperation, targetInputs, allowSideEffects);
            return ToolResult(plan);
        }

        return tool.Binding.OperationId switch
        {
            "api.surface" => ToolResult(CompanionOperationSurface.Create()),
            "workspace.guide" => ToolResult(SourceWorkspaceGuide.Evaluate(ValueOrNull(inputs, "root"))),
            "devices.list" => ToolResult(new
            {
                schemaVersion = "rusty.xr.companion.devices.v1",
                devices = await new QuestAdbService().ListDevicesAsync(cancellationToken).ConfigureAwait(false)
            }),
            "broker.status" => await BrokerStatusToolAsync(inputs, cancellationToken).ConfigureAwait(false),
            "core.quest_app_catalog_schema" => ToolResult(await new CatalogLoader()
                .LoadAsync(Required(inputs, "path"), cancellationToken)
                .ConfigureAwait(false)),
            _ => BlockedPlan(tool.Binding.OperationId, inputs)
        };
    }

    private static async Task<object> BrokerStatusToolAsync(
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken)
    {
        var port = inputs.TryGetValue("port", out var rawPort) && int.TryParse(rawPort, out var parsedPort)
            ? parsedPort
            : BrokerClientService.DefaultPort;
        var uri = BrokerClientService.CreateStatusUri(
            explicitUrl: null,
            host: ValueOrNull(inputs, "host"),
            port: port);
        return ToolResult(await new BrokerClientService().GetStatusAsync(uri, cancellationToken).ConfigureAwait(false));
    }

    private static object BlockedPlan(string operationId, IReadOnlyDictionary<string, string> inputs)
    {
        var plan = CompanionOperationPlanner.CreatePlan(operationId, inputs);
        return ToolResult(
            plan,
            isError: true,
            "This MCP server does not execute side-effecting operations directly. Review the dispatch plan and rerun through a confirmed execution path.");
    }

    private static object ToolResult(object structuredContent, bool isError = false, string? message = null)
    {
        var text = JsonSerializer.Serialize(structuredContent, JsonOptions);
        if (!string.IsNullOrWhiteSpace(message))
        {
            text = message + Environment.NewLine + Environment.NewLine + text;
        }

        return new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text
                }
            },
            structuredContent,
            isError
        };
    }

    private static IReadOnlyDictionary<string, string> ParseNestedInputs(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty("inputs", out var inputsElement) ||
            inputsElement.ValueKind == JsonValueKind.Null ||
            inputsElement.ValueKind == JsonValueKind.Undefined)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        if (inputsElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("rusty_xr_operation_plan arguments.inputs must be an object.");
        }

        return ToInputs(inputsElement);
    }

    private static IReadOnlyDictionary<string, string> ToInputs(JsonElement arguments)
    {
        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return inputs;
        }

        foreach (var property in arguments.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            inputs[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.GetRawText();
        }

        return inputs;
    }

    private static string Required(IReadOnlyDictionary<string, string> inputs, string name)
        => inputs.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Input '{name}' is required.");

    private static string? ValueOrNull(IReadOnlyDictionary<string, string> inputs, string name)
        => inputs.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static bool BoolInput(IReadOnlyDictionary<string, string> inputs, string name)
        => inputs.TryGetValue(name, out var value) && bool.TryParse(value, out var parsed) && parsed;

    private static JsonObject Error(int code, string message)
        => new()
        {
            ["code"] = code,
            ["message"] = message
        };

    private static string SerializeResponse(JsonRpcResponse response)
    {
        var root = new JsonObject
        {
            ["jsonrpc"] = "2.0"
        };

        root["id"] = response.Id.HasValue
            ? JsonNode.Parse(response.Id.Value.GetRawText())
            : null;
        if (response.Error is not null)
        {
            root["error"] = response.Error;
        }
        else
        {
            root["result"] = JsonSerializer.SerializeToNode(response.Result, JsonOptions);
        }

        return root.ToJsonString(JsonOptions);
    }

    private sealed record JsonRpcResponse(
        JsonElement? Id,
        object? Result,
        JsonObject? Error);
}
