using System.Text;
using System.Text.Json.Nodes;

namespace RustyXr.Companion.Core;

public static class CompanionOperationSurface
{
    public const string SchemaVersion = "rusty.xr.companion.operation-surface.v1";
    public const string McpToolsSchemaVersion = "rusty.xr.companion.mcp-tools.v1";

    private const string WindowsOwner = "windows-companion";
    private const string AndroidOwner = "android-companion";
    private const string CoreOwner = "rusty-xr-core";

    public static CompanionOperationCatalog Create()
        => new(
            SchemaVersion,
            new[]
            {
                new CompanionOperation(
                    "api.surface",
                    WindowsOwner,
                    "Operation surface",
                    "Lists the versioned Rusty XR operation catalog used to align API, CLI, and MCP tool names.",
                    "CompanionOperationSurface.Create",
                    "api surface [--json|--mcp-tools]",
                    "rusty_xr_api_surface",
                    "read-only",
                    Array.Empty<CompanionOperationParameter>(),
                    new[] { SchemaVersion, McpToolsSchemaVersion, CompanionOperationPlanner.SchemaVersion },
                    "Bootstrap operation for MCP servers and local agents."),
                new CompanionOperation(
                    "api.plan",
                    WindowsOwner,
                    "Operation dispatch plan",
                    "Builds an inspectable command plan for a known Rusty XR operation without executing it.",
                    "CompanionOperationPlanner.CreatePlan",
                    "api plan --operation <id> [--arg key=value] [--allow-side-effects] --json",
                    "rusty_xr_operation_plan",
                    "read-only",
                    new[]
                    {
                        Parameter("operation", "string", required: true, "Operation id to plan.", "broker.status"),
                        Parameter("inputs", "object", required: false, "Operation inputs as a flat JSON object.", "{\"host\":\"127.0.0.1\"}"),
                        Parameter("allowSideEffects", "boolean", required: false, "Include the CLI opt-in flag for side-effecting operation plans.", "false")
                    },
                    new[] { CompanionOperationPlanner.SchemaVersion },
                    "MCP servers should call the planner before executing side-effecting operations."),
                new CompanionOperation(
                    "workspace.guide",
                    WindowsOwner,
                    "Source workspace guide",
                    "Evaluates the sibling Rusty XR and Companion source layout and returns expected build, catalog, APK, and verification commands.",
                    "SourceWorkspaceGuide.Evaluate",
                    "workspace guide --root <workspace> --json",
                    "rusty_xr_workspace_guide",
                    "read-only",
                    new[]
                    {
                        Parameter("root", "path", required: false, "Workspace root containing sibling public repos.", ".\\workspace")
                    },
                    new[] { "SourceWorkspaceStatus" },
                    "Use before source-build or catalog-path automation."),
                new CompanionOperation(
                    "doctor",
                    WindowsOwner,
                    "Windows companion doctor",
                    "Analyzes local Windows, companion tooling, and optional snapshots for shareable diagnostics.",
                    "WindowsEnvironmentAnalyzer.AnalyzeAsync",
                    "doctor [--snapshots] [--out <folder>] --json",
                    "rusty_xr_doctor",
                    "local-read-write",
                    new[]
                    {
                        Parameter("snapshots", "boolean", required: false, "Include extra environment snapshots.", "true"),
                        Parameter("out", "path", required: false, "Folder for a diagnostics bundle.", ".\\artifacts\\diagnostics")
                    },
                    new[] { "DiagnosticsReport", "DiagnosticsBundle" }),
                new CompanionOperation(
                    "devices.list",
                    WindowsOwner,
                    "List Quest devices",
                    "Lists Quest devices visible through companion-managed or user-supplied ADB.",
                    "QuestAdbService.ListDevicesAsync",
                    "devices --json",
                    "rusty_xr_list_devices",
                    "read-only-device",
                    Array.Empty<CompanionOperationParameter>(),
                    new[] { "QuestDevice[]" }),
                new CompanionOperation(
                    "catalog.verify",
                    WindowsOwner,
                    "Verify catalog app",
                    "Installs, launches, logs, and writes a verification bundle for a catalog app and runtime profile.",
                    "CatalogLoader + QuestAdbService + RuntimeProfileLogValidator",
                    "catalog verify --path <catalog.json> --app <id> --serial <serial> [--install] [--launch] [--runtime-profile <id>] [--out <folder>] --json",
                    "rusty_xr_verify_catalog",
                    "device-state-changing",
                    new[]
                    {
                        Parameter("path", "path", required: true, "Quest app catalog JSON path.", "samples\\quest-session-kit\\apk-catalog.example.json"),
                        Parameter("app", "string", required: true, "Catalog app id.", "rusty-xr-quest-broker"),
                        Parameter("serial", "string", required: true, "ADB device serial.", "ABC123"),
                        Parameter("install", "boolean", required: false, "Install the selected APK before verification.", "true"),
                        Parameter("launch", "boolean", required: false, "Launch the selected app before verification.", "true"),
                        Parameter("runtimeProfile", "string", required: false, "Runtime profile id.", "broker-osc-drive-ingress"),
                        Parameter("out", "path", required: false, "Verification output folder.", ".\\artifacts\\verify")
                    },
                    new[] { "CatalogVerificationReport" },
                    "MCP wrappers should require explicit user intent for install or launch."),
                new CompanionOperation(
                    "apk.install",
                    WindowsOwner,
                    "Install APK",
                    "Installs a user-supplied APK onto a selected Quest over ADB.",
                    "QuestAdbService.InstallAsync",
                    "install --serial <serial> --apk <path>",
                    "rusty_xr_install_apk",
                    "device-state-changing",
                    new[]
                    {
                        Parameter("serial", "string", required: true, "ADB device serial.", "ABC123"),
                        Parameter("apk", "path", required: true, "User-supplied APK path.", ".\\build\\outputs\\app-debug.apk")
                    },
                    new[] { "CommandResult" }),
                new CompanionOperation(
                    "profile.launch",
                    WindowsOwner,
                    "Launch runtime profile",
                    "Launches a target app directly or through a catalog runtime profile.",
                    "QuestAdbService.LaunchAsync",
                    "catalog launch --path <catalog.json> --app <id> --serial <serial> [--runtime-profile <id>]",
                    "rusty_xr_launch_profile",
                    "device-state-changing",
                    new[]
                    {
                        Parameter("path", "path", required: true, "Quest app catalog JSON path.", "catalogs\\rusty-xr-quest-composite-layer.catalog.json"),
                        Parameter("app", "string", required: true, "Catalog app id.", "rusty-xr-quest-composite-layer"),
                        Parameter("serial", "string", required: true, "ADB device serial.", "ABC123"),
                        Parameter("runtimeProfile", "string", required: false, "Runtime profile id.", "camera-stereo-gpu-composite")
                    },
                    new[] { "CommandResult" },
                    "Hazardous catalog profiles must remain explicitly gated."),
                new CompanionOperation(
                    "broker.status",
                    WindowsOwner,
                    "Broker status",
                    "Reads the forwarded Rusty XR broker HTTP status endpoint.",
                    "BrokerClientService.GetStatusAsync",
                    "broker status [--host 127.0.0.1] [--port <n>] --json",
                    "rusty_xr_broker_status",
                    "read-only-device",
                    new[]
                    {
                        Parameter("host", "string", required: false, "Forwarded broker host.", "127.0.0.1"),
                        Parameter("port", "integer", required: false, "Forwarded broker HTTP port.", "8765")
                    },
                    new[] { "BrokerStatusJson" }),
                new CompanionOperation(
                    "broker.compare",
                    WindowsOwner,
                    "Broker route comparison",
                    "Writes a direct OSC versus broker-routed OSC/WebSocket timing bundle.",
                    "BrokerComparisonService.RunAsync",
                    "broker compare --quest-host <quest-ip> [--serial <serial>] [--out <folder>] --json",
                    "rusty_xr_broker_compare",
                    "device-commanding",
                    new[]
                    {
                        Parameter("questHost", "string", required: true, "Quest LAN IP or host.", "192.168.1.25"),
                        Parameter("serial", "string", required: false, "ADB device serial for forwarding and snapshots.", "ABC123"),
                        Parameter("count", "integer", required: false, "Sample count.", "16"),
                        Parameter("intervalMs", "integer", required: false, "Delay between samples in milliseconds.", "250"),
                        Parameter("out", "path", required: false, "Output folder for JSON, Markdown, and CSV reports.", ".\\artifacts\\broker-compare")
                    },
                    new[] { "BrokerComparisonReport" }),
                new CompanionOperation(
                    "broker.h264_proxy_probe",
                    WindowsOwner,
                    "Broker H.264 TCP proxy probe",
                    "Runs a bounded broker-local synthetic RXYRVID1 H.264 TCP proxy probe and reports forwarding metrics.",
                    "BrokerClientService.SendCommandAsync(media.run_h264_tcp_proxy_probe)",
                    "broker h264-proxy-probe [--serial <serial>] [--packet-count <n>] [--packet-bytes <n>] [--width <n>] [--height <n>] [--timeout-ms <n>] --json",
                    "rusty_xr_broker_h264_proxy_probe",
                    "device-commanding",
                    new[]
                    {
                        Parameter("serial", "string", required: false, "ADB device serial for forwarding before the probe.", "ABC123"),
                        Parameter("brokerHost", "string", required: false, "Forwarded broker host.", "127.0.0.1"),
                        Parameter("brokerHostPort", "integer", required: false, "Host-side forwarded broker port.", "8765"),
                        Parameter("brokerDevicePort", "integer", required: false, "Device-side broker port.", "8765"),
                        Parameter("packetCount", "integer", required: false, "Synthetic packet count.", "4"),
                        Parameter("packetBytes", "integer", required: false, "Synthetic payload bytes per packet.", "96"),
                        Parameter("width", "integer", required: false, "Synthetic encoded stream width.", "64"),
                        Parameter("height", "integer", required: false, "Synthetic encoded stream height.", "64"),
                        Parameter("timeoutMs", "integer", required: false, "Probe timeout in milliseconds.", "10000")
                    },
                    new[] { "rusty.xr.broker.h264_tcp_proxy_probe.v1" },
                    "This opens bounded loopback sockets on the broker device and should remain an explicit diagnostic action."),
                new CompanionOperation(
                    "broker.h264_proxy_start",
                    WindowsOwner,
                    "Start broker H.264 TCP proxy",
                    "Commands a broker to subscribe to a remote RXYRVID1 H.264 TCP stream and republish it on a local endpoint.",
                    "BrokerClientService.SendCommandAsync(media.start_h264_tcp_proxy)",
                    "broker h264-proxy-start --remote-host <host> [--serial <serial>] [--remote-port <n>] [--local-port <n>] [--local-bind-host <host>] [--local-lan-enabled] --json",
                    "rusty_xr_broker_h264_proxy_start",
                    "device-commanding",
                    new[]
                    {
                        Parameter("remoteHost", "string", required: true, "Remote broker H.264 stream host.", "192.168.1.25"),
                        Parameter("serial", "string", required: false, "ADB device serial for forwarding before the command.", "ABC123"),
                        Parameter("brokerHost", "string", required: false, "Forwarded broker control host.", "127.0.0.1"),
                        Parameter("brokerHostPort", "integer", required: false, "Host-side forwarded broker control port.", "8765"),
                        Parameter("brokerDevicePort", "integer", required: false, "Device-side broker control port.", "8765"),
                        Parameter("remotePort", "integer", required: false, "Remote broker H.264 stream port.", "8879"),
                        Parameter("localPort", "integer", required: false, "Local broker republished stream port.", "8879"),
                        Parameter("localHostPort", "integer", required: false, "Host-side port metadata for the local stream.", "18879"),
                        Parameter("localBindHost", "string", required: false, "Local bind host; non-loopback requires localLanEnabled.", "127.0.0.1"),
                        Parameter("localLanEnabled", "boolean", required: false, "Allow a non-loopback local bind for LAN experiments.", "false"),
                        Parameter("connectTimeoutMs", "integer", required: false, "Remote connect timeout in milliseconds.", "15000"),
                        Parameter("acceptTimeoutMs", "integer", required: false, "Local consumer accept timeout in milliseconds.", "30000"),
                        Parameter("timeoutMs", "integer", required: false, "Command reply timeout in milliseconds.", "30000")
                    },
                    new[] { "rusty.xr.broker.h264_tcp_proxy_start.v1" },
                    "This is a network-listener diagnostic. Non-loopback local binds must be explicitly opted in."),
                new CompanionOperation(
                    "media.inspect_h264",
                    WindowsOwner,
                    "Inspect H.264 artifact",
                    "Inspects a saved H.264 payload and optionally probes first-frame decode through an external FFmpeg sidecar.",
                    "EncodedVideoArtifactInspectionService.InspectAsync",
                    "media inspect-h264 --payload <file.h264> [--decode] [--ffmpeg <path>] --json",
                    "rusty_xr_inspect_h264",
                    "local-read-write",
                    new[]
                    {
                        Parameter("payload", "path", required: true, "Saved H.264 payload path.", ".\\artifacts\\camera.h264"),
                        Parameter("decode", "boolean", required: false, "Run an external first-frame decoder probe.", "false"),
                        Parameter("ffmpeg", "path", required: false, "User-supplied ffmpeg executable.", "ffmpeg.exe")
                    },
                    new[] { "EncodedVideoArtifactInspection" }),
                new CompanionOperation(
                    "core.quest_app_catalog_schema",
                    CoreOwner,
                    "Quest app catalog schema",
                    "Shared public catalog schema shape consumed by Rusty XR examples, Windows Companion, and Android Companion.",
                    "rusty.xr.quest-app-catalog.v1",
                    "catalog list --path <catalog.json> --json",
                    "rusty_xr_read_catalog",
                    "read-only",
                    new[]
                    {
                        Parameter("path", "path", required: true, "Quest app catalog JSON path.", "catalogs\\rusty-xr-quest-composite-layer.catalog.json")
                    },
                    new[] { "QuestSessionCatalog" }),
                new CompanionOperation(
                    "android.agent_command",
                    AndroidOwner,
                    "Android companion agent command",
                    "Runs a time-limited, phone-gated Android Companion command activity and writes a JSON report on the phone.",
                    "AgentCommandActivity",
                    "adb -s <phone-serial> shell am start -a <action> -n <component> --es command <command> ...",
                    "rusty_xr_android_agent_command",
                    "phone-agent-gated",
                    new[]
                    {
                        Parameter("phoneSerial", "string", required: true, "ADB serial for the Android phone.", "PHONE123"),
                        Parameter("command", "string", required: true, "Android agent command name.", "quest-install"),
                        Parameter("endpoint", "string", required: false, "Quest Wi-Fi ADB endpoint.", "192.168.1.25:5555"),
                        Parameter("timeoutMs", "integer", required: false, "Command timeout in milliseconds for command families that accept one.", "45000"),
                        Parameter("oscPort", "integer", required: false, "OSC UDP port for agent commands that probe OSC.", "9000"),
                        Parameter("deviceAddress", "string", required: false, "BLE device address for Polar PMD smoke commands.", "00:11:22:33:44:55"),
                        Parameter("apkFile", "string", required: false, "Phone-local staged APK file name.", "target-app.apk"),
                        Parameter("packageId", "string", required: false, "Target package id from public catalog or user input.", "com.example.target"),
                        Parameter("component", "string", required: false, "Explicit launch component when needed.", "com.example.target/.MainActivity"),
                        Parameter("runtimeExtras", "object", required: false, "Runtime launch extras.", "{\"rustyxr.camera\":\"false\"}"),
                        Parameter("utility", "string", required: false, "Utility name for quest-utility commands.", "wake"),
                        Parameter("allowDevSession", "boolean", required: false, "Open a debug-only temporary command window.", "false")
                    },
                    new[] { "rusty.xr.android-companion.agent-command.v1" },
                    "Requires Agent Command Mode to be enabled in the phone app unless a debug validation window is explicitly opened.",
                    "io.github.mesmerprism.rustyxr.companion.android.RUN_AGENT_COMMAND",
                    "io.github.mesmerprism.rustyxr.companion.android/.agent.AgentCommandActivity")
            });

    public static CompanionOperationCatalog Filter(CompanionOperationCatalog catalog, string? owner)
    {
        var normalized = NormalizeOwner(owner);
        if (normalized is null)
        {
            return catalog;
        }

        return catalog with
        {
            Operations = catalog.Operations
                .Where(operation => string.Equals(operation.Owner, normalized, StringComparison.OrdinalIgnoreCase))
                .ToArray()
        };
    }

    public static CompanionMcpToolList ToMcpToolList(CompanionOperationCatalog catalog)
        => new(
            McpToolsSchemaVersion,
            catalog.Operations.Select(operation =>
            {
                var mutates = IsMutating(operation.Safety);
                return new CompanionMcpTool(
                    operation.McpToolName,
                    operation.Summary,
                    BuildInputSchema(operation.Parameters),
                    new CompanionMcpToolAnnotations(
                        ReadOnlyHint: !mutates && !operation.Safety.Contains("write", StringComparison.OrdinalIgnoreCase),
                        DestructiveHint: mutates,
                        IdempotentHint: operation.Safety is "read-only" or "read-only-device",
                        OpenWorldHint: operation.Owner is WindowsOwner or AndroidOwner),
                    new CompanionOperationBinding(
                        operation.Id,
                        operation.Owner,
                        operation.ApiSurface,
                        operation.CliTemplate,
                        operation.AndroidIntentAction,
                        operation.AndroidIntentComponent,
                        operation.Safety));
            }).ToArray());

    public static string ToMarkdown(CompanionOperationCatalog catalog)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Rusty XR Companion Operation Surface");
        builder.AppendLine();
        builder.AppendLine($"Schema: `{catalog.SchemaVersion}`");
        builder.AppendLine();
        builder.AppendLine("This catalog is the shared naming layer for the reusable API surface, the human CLI, and the MCP server wrapper.");
        builder.AppendLine();

        foreach (var operation in catalog.Operations)
        {
            builder.AppendLine($"## {operation.Id}");
            builder.AppendLine();
            builder.AppendLine(operation.Summary);
            builder.AppendLine();
            builder.AppendLine($"- owner: `{operation.Owner}`");
            builder.AppendLine($"- API: `{operation.ApiSurface}`");
            builder.AppendLine($"- CLI: `{operation.CliTemplate}`");
            builder.AppendLine($"- MCP tool: `{operation.McpToolName}`");
            builder.AppendLine($"- safety: `{operation.Safety}`");
            if (!string.IsNullOrWhiteSpace(operation.Notes))
            {
                builder.AppendLine($"- notes: {operation.Notes}");
            }

            if (operation.Parameters.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Parameters:");
                foreach (var parameter in operation.Parameters)
                {
                    var required = parameter.Required ? "required" : "optional";
                    builder.AppendLine($"- `{parameter.Name}` ({parameter.Kind}, {required}): {parameter.Description}");
                }
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static CompanionOperationParameter Parameter(
        string name,
        string kind,
        bool required,
        string description,
        string? example = null)
        => new(name, kind, required, description, example);

    private static string? NormalizeOwner(string? owner)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return owner.Trim().ToLowerInvariant() switch
        {
            "windows" or "windows-companion" or "companion" => WindowsOwner,
            "android" or "android-companion" or "phone" => AndroidOwner,
            "core" or "rusty-xr" or "rusty-xr-core" => CoreOwner,
            var value => value
        };
    }

    private static bool IsMutating(string safety)
        => safety.Contains("state", StringComparison.OrdinalIgnoreCase) ||
           safety.Contains("command", StringComparison.OrdinalIgnoreCase) ||
           safety.Contains("gated", StringComparison.OrdinalIgnoreCase);

    private static JsonObject BuildInputSchema(IReadOnlyList<CompanionOperationParameter> parameters)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var parameter in parameters)
        {
            var property = new JsonObject
            {
                ["type"] = JsonType(parameter.Kind),
                ["description"] = parameter.Description
            };
            if (!string.IsNullOrWhiteSpace(parameter.Example))
            {
                property["examples"] = new JsonArray(parameter.Example);
            }

            properties[parameter.Name] = property;
            if (parameter.Required)
            {
                required.Add(parameter.Name);
            }
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
            ["required"] = required
        };
    }

    private static string JsonType(string kind)
        => kind switch
        {
            "boolean" => "boolean",
            "integer" => "integer",
            "object" => "object",
            _ => "string"
        };
}

public sealed record CompanionOperationCatalog(
    string SchemaVersion,
    IReadOnlyList<CompanionOperation> Operations);

public sealed record CompanionOperation(
    string Id,
    string Owner,
    string Title,
    string Summary,
    string ApiSurface,
    string CliTemplate,
    string McpToolName,
    string Safety,
    IReadOnlyList<CompanionOperationParameter> Parameters,
    IReadOnlyList<string> OutputSchemas,
    string Notes = "",
    string? AndroidIntentAction = null,
    string? AndroidIntentComponent = null);

public sealed record CompanionOperationParameter(
    string Name,
    string Kind,
    bool Required,
    string Description,
    string? Example = null);

public sealed record CompanionMcpToolList(
    string SchemaVersion,
    IReadOnlyList<CompanionMcpTool> Tools);

public sealed record CompanionMcpTool(
    string Name,
    string Description,
    JsonObject InputSchema,
    CompanionMcpToolAnnotations Annotations,
    CompanionOperationBinding Binding);

public sealed record CompanionMcpToolAnnotations(
    bool ReadOnlyHint,
    bool DestructiveHint,
    bool IdempotentHint,
    bool OpenWorldHint);

public sealed record CompanionOperationBinding(
    string OperationId,
    string Owner,
    string ApiSurface,
    string CliTemplate,
    string? AndroidIntentAction,
    string? AndroidIntentComponent,
    string Safety);
