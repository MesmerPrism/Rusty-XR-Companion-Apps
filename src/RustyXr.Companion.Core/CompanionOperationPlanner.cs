using System.Text;
using System.Text.Json;

namespace RustyXr.Companion.Core;

public static class CompanionOperationPlanner
{
    public const string SchemaVersion = "rusty.xr.companion.operation-plan.v1";

    private const string CompanionCliExecutable = "rusty-xr-companion";
    private const string AndroidAgentAction = "io.github.mesmerprism.rustyxr.companion.android.RUN_AGENT_COMMAND";
    private const string AndroidAgentComponent = "io.github.mesmerprism.rustyxr.companion.android/.agent.AgentCommandActivity";

    public static CompanionOperationPlan CreatePlan(
        string operationId,
        IReadOnlyDictionary<string, string> inputs,
        bool allowSideEffects = false)
    {
        var catalog = CompanionOperationSurface.Create();
        var operation = catalog.Operations.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, operationId, StringComparison.OrdinalIgnoreCase));
        if (operation is null)
        {
            throw new ArgumentException($"Unknown operation '{operationId}'.", nameof(operationId));
        }

        var normalizedInputs = ValidateInputs(operation, inputs);
        var command = BuildCommand(operation.Id, normalizedInputs);
        var requiresSideEffectOptIn = !IsReadOnly(operation.Safety);
        var allowed = !requiresSideEffectOptIn || allowSideEffects;
        var warnings = new List<string>();
        if (requiresSideEffectOptIn)
        {
            warnings.Add("Operation may write local files, command a device, or cross a phone/app command gate.");
        }

        if (operation.Id == "android.agent_command")
        {
            warnings.Add("Android Agent Command Mode must be enabled in the phone app unless a debug dev session is explicitly requested.");
            if (TryGetBoolean(normalizedInputs, "allowDevSession"))
            {
                warnings.Add("allowDevSession is for debug validation windows and should not be used as a release automation bypass.");
            }
        }

        var gateReason = allowed
            ? requiresSideEffectOptIn
                ? "Side-effect opt-in supplied; execution still belongs to a runner-level confirmation path."
                : "Read-only operation."
            : "Side-effect opt-in required before execution.";

        return new CompanionOperationPlan(
            SchemaVersion,
            operation.Id,
            operation.McpToolName,
            operation.Owner,
            operation.Safety,
            allowed,
            requiresSideEffectOptIn,
            gateReason,
            command.Executable,
            command.Arguments,
            FormatCommand(command.Executable, command.Arguments),
            normalizedInputs,
            warnings,
            operation.AndroidIntentAction,
            operation.AndroidIntentComponent,
            KioskCommandRunRecords.CreatePlanTemplate(operation.Id, FormatCommand(command.Executable, command.Arguments)));
    }

    public static string ToMarkdown(CompanionOperationPlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Rusty XR Operation Plan");
        builder.AppendLine();
        builder.AppendLine($"Schema: `{plan.SchemaVersion}`");
        builder.AppendLine($"Operation: `{plan.OperationId}`");
        builder.AppendLine($"MCP tool: `{plan.McpToolName}`");
        builder.AppendLine($"Owner: `{plan.Owner}`");
        builder.AppendLine($"Safety: `{plan.Safety}`");
        builder.AppendLine($"Allowed: `{plan.Allowed}`");
        builder.AppendLine($"Requires side-effect opt-in: `{plan.RequiresSideEffectOptIn}`");
        builder.AppendLine($"Gate: {plan.GateReason}");
        builder.AppendLine();
        builder.AppendLine("Command:");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(plan.DisplayCommand);
        builder.AppendLine("```");

        if (plan.Inputs.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Inputs:");
            foreach (var input in plan.Inputs.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                builder.AppendLine($"- `{input.Key}`: `{input.Value}`");
            }
        }

        if (plan.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Warnings:");
            foreach (var warning in plan.Warnings)
            {
                builder.AppendLine($"- {warning}");
            }
        }

        if (plan.KioskCommandRunRecordTemplate.HasValue)
        {
            builder.AppendLine();
            builder.AppendLine("Run record:");
            builder.AppendLine($"- `{KioskCommandRunRecords.CommandRunRecordSchema}` template will be emitted by the execution path.");
        }

        return builder.ToString().TrimEnd();
    }

    private static CompanionCommandPlan BuildCommand(string operationId, IReadOnlyDictionary<string, string> inputs)
    {
        var arguments = new List<string>();
        string executable = CompanionCliExecutable;

        switch (operationId)
        {
            case "api.surface":
                Add(arguments, "api", "surface", "--json");
                break;

            case "api.plan":
                Add(arguments, "api", "plan");
                AddOption(arguments, "--operation", Required(inputs, "operation"));
                AddJsonInputsAsArgs(arguments, ValueOrNull(inputs, "inputs"));
                AddFlag(arguments, "--allow-side-effects", TryGetBoolean(inputs, "allowSideEffects"));
                Add(arguments, "--json");
                break;

            case "workspace.guide":
                Add(arguments, "workspace", "guide", "--json");
                AddOption(arguments, "--root", ValueOrNull(inputs, "root"));
                break;

            case "doctor":
                Add(arguments, "doctor", "--json");
                AddFlag(arguments, "--snapshots", TryGetBoolean(inputs, "snapshots"));
                AddOption(arguments, "--out", ValueOrNull(inputs, "out"));
                break;

            case "devices.list":
                Add(arguments, "devices", "--json");
                break;

            case "catalog.verify":
                Add(arguments, "catalog", "verify");
                AddOption(arguments, "--path", Required(inputs, "path"));
                AddOption(arguments, "--app", Required(inputs, "app"));
                AddOption(arguments, "--serial", Required(inputs, "serial"));
                AddFlag(arguments, "--install", TryGetBoolean(inputs, "install"));
                AddFlag(arguments, "--launch", TryGetBoolean(inputs, "launch"));
                AddOption(arguments, "--runtime-profile", ValueOrNull(inputs, "runtimeProfile"));
                AddOption(arguments, "--out", ValueOrNull(inputs, "out"));
                Add(arguments, "--json");
                break;

            case "apk.install":
                Add(arguments, "install");
                AddOption(arguments, "--serial", Required(inputs, "serial"));
                AddOption(arguments, "--apk", Required(inputs, "apk"));
                break;

            case "profile.launch":
                Add(arguments, "catalog", "launch");
                AddOption(arguments, "--path", Required(inputs, "path"));
                AddOption(arguments, "--app", Required(inputs, "app"));
                AddOption(arguments, "--serial", Required(inputs, "serial"));
                AddOption(arguments, "--runtime-profile", ValueOrNull(inputs, "runtimeProfile"));
                break;

            case "broker.status":
                Add(arguments, "broker", "status");
                AddOption(arguments, "--host", ValueOrNull(inputs, "host"));
                AddOption(arguments, "--port", ValueOrNull(inputs, "port"));
                Add(arguments, "--json");
                break;

            case "broker.compare":
                Add(arguments, "broker", "compare");
                AddOption(arguments, "--quest-host", Required(inputs, "questHost"));
                AddOption(arguments, "--serial", ValueOrNull(inputs, "serial"));
                AddOption(arguments, "--count", ValueOrNull(inputs, "count"));
                AddOption(arguments, "--interval-ms", ValueOrNull(inputs, "intervalMs"));
                AddOption(arguments, "--out", ValueOrNull(inputs, "out"));
                Add(arguments, "--json");
                break;

            case "broker.h264_proxy_probe":
                Add(arguments, "broker", "h264-proxy-probe");
                AddOption(arguments, "--serial", ValueOrNull(inputs, "serial"));
                AddOption(arguments, "--broker-host", ValueOrNull(inputs, "brokerHost"));
                AddOption(arguments, "--broker-host-port", ValueOrNull(inputs, "brokerHostPort"));
                AddOption(arguments, "--broker-device-port", ValueOrNull(inputs, "brokerDevicePort"));
                AddOption(arguments, "--packet-count", ValueOrNull(inputs, "packetCount"));
                AddOption(arguments, "--packet-bytes", ValueOrNull(inputs, "packetBytes"));
                AddOption(arguments, "--width", ValueOrNull(inputs, "width"));
                AddOption(arguments, "--height", ValueOrNull(inputs, "height"));
                AddOption(arguments, "--timeout-ms", ValueOrNull(inputs, "timeoutMs"));
                Add(arguments, "--json");
                break;

            case "broker.h264_proxy_start":
                Add(arguments, "broker", "h264-proxy-start");
                AddOption(arguments, "--remote-host", Required(inputs, "remoteHost"));
                AddOption(arguments, "--serial", ValueOrNull(inputs, "serial"));
                AddOption(arguments, "--broker-host", ValueOrNull(inputs, "brokerHost"));
                AddOption(arguments, "--broker-host-port", ValueOrNull(inputs, "brokerHostPort"));
                AddOption(arguments, "--broker-device-port", ValueOrNull(inputs, "brokerDevicePort"));
                AddOption(arguments, "--remote-port", ValueOrNull(inputs, "remotePort"));
                AddOption(arguments, "--local-port", ValueOrNull(inputs, "localPort"));
                AddOption(arguments, "--local-host-port", ValueOrNull(inputs, "localHostPort"));
                AddOption(arguments, "--local-bind-host", ValueOrNull(inputs, "localBindHost"));
                AddFlag(arguments, "--local-lan-enabled", TryGetBoolean(inputs, "localLanEnabled"));
                AddOption(arguments, "--connect-timeout-ms", ValueOrNull(inputs, "connectTimeoutMs"));
                AddOption(arguments, "--accept-timeout-ms", ValueOrNull(inputs, "acceptTimeoutMs"));
                AddOption(arguments, "--timeout-ms", ValueOrNull(inputs, "timeoutMs"));
                Add(arguments, "--json");
                break;

            case "media.inspect_h264":
                Add(arguments, "media", "inspect-h264");
                AddOption(arguments, "--payload", Required(inputs, "payload"));
                AddFlag(arguments, "--decode", TryGetBoolean(inputs, "decode"));
                AddOption(arguments, "--ffmpeg", ValueOrNull(inputs, "ffmpeg"));
                Add(arguments, "--json");
                break;

            case "core.quest_app_catalog_schema":
                Add(arguments, "catalog", "list");
                AddOption(arguments, "--path", Required(inputs, "path"));
                Add(arguments, "--json");
                break;

            case "android.agent_command":
                executable = "adb";
                Add(arguments, "-s", Required(inputs, "phoneSerial"));
                Add(arguments, "shell", "am", "start");
                Add(arguments, "-a", AndroidAgentAction);
                Add(arguments, "-n", AndroidAgentComponent);
                AddAndroidStringExtra(arguments, "command", Required(inputs, "command"));
                AddAndroidStringExtra(arguments, "endpoint", ValueOrNull(inputs, "endpoint"));
                AddAndroidLongExtra(arguments, "timeout_ms", ValueOrNull(inputs, "timeoutMs"));
                AddAndroidIntegerExtra(arguments, "osc_port", ValueOrNull(inputs, "oscPort"));
                AddAndroidStringExtra(arguments, "device_address", ValueOrNull(inputs, "deviceAddress"));
                AddAndroidStringExtra(arguments, "apk_file", ValueOrNull(inputs, "apkFile"));
                AddAndroidStringExtra(arguments, "package_id", ValueOrNull(inputs, "packageId"));
                AddAndroidStringExtra(arguments, "component", ValueOrNull(inputs, "component"));
                AddAndroidStringExtra(arguments, "extras_json", ValueOrNull(inputs, "runtimeExtras"));
                AddAndroidStringExtra(arguments, "utility", ValueOrNull(inputs, "utility"));
                AddAndroidBooleanExtra(arguments, "allow_dev_session", TryGetBoolean(inputs, "allowDevSession"));
                break;

            default:
                throw new ArgumentException($"Operation '{operationId}' does not have a dispatch plan yet.");
        }

        return new CompanionCommandPlan(executable, arguments.ToArray());
    }

    private static IReadOnlyDictionary<string, string> ValidateInputs(
        CompanionOperation operation,
        IReadOnlyDictionary<string, string> inputs)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            if (!string.IsNullOrWhiteSpace(input.Key) && !string.IsNullOrWhiteSpace(input.Value))
            {
                normalized[input.Key.Trim()] = input.Value.Trim();
            }
        }

        var parameterNames = operation.Parameters
            .Select(static parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var input in normalized.Keys)
        {
            if (!parameterNames.Contains(input))
            {
                throw new ArgumentException($"Operation '{operation.Id}' does not accept input '{input}'.");
            }
        }

        foreach (var parameter in operation.Parameters)
        {
            if (parameter.Required && !normalized.ContainsKey(parameter.Name))
            {
                throw new ArgumentException($"Operation '{operation.Id}' requires input '{parameter.Name}'.");
            }

            if (!normalized.TryGetValue(parameter.Name, out var value))
            {
                continue;
            }

            switch (parameter.Kind)
            {
                case "boolean":
                    if (!bool.TryParse(value, out _))
                    {
                        throw new ArgumentException($"Input '{parameter.Name}' must be true or false.");
                    }
                    break;

                case "integer":
                    if (!long.TryParse(value, out _))
                    {
                        throw new ArgumentException($"Input '{parameter.Name}' must be an integer.");
                    }
                    break;

                case "object":
                    using (JsonDocument.Parse(value))
                    {
                    }
                    break;
            }
        }

        return normalized;
    }

    private static bool IsReadOnly(string safety)
        => safety is "read-only" or "read-only-device";

    private static string Required(IReadOnlyDictionary<string, string> inputs, string name)
        => inputs.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Input '{name}' is required.");

    private static string? ValueOrNull(IReadOnlyDictionary<string, string> inputs, string name)
        => inputs.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static bool TryGetBoolean(IReadOnlyDictionary<string, string> inputs, string name)
        => inputs.TryGetValue(name, out var value) && bool.TryParse(value, out var parsed) && parsed;

    private static void Add(List<string> arguments, params string[] values)
        => arguments.AddRange(values);

    private static void AddOption(List<string> arguments, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        arguments.Add(name);
        arguments.Add(value);
    }

    private static void AddFlag(List<string> arguments, string name, bool enabled)
    {
        if (enabled)
        {
            arguments.Add(name);
        }
    }

    private static void AddAndroidStringExtra(List<string> arguments, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        Add(arguments, "--es", name, value);
    }

    private static void AddAndroidIntegerExtra(List<string> arguments, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        Add(arguments, "--ei", name, value);
    }

    private static void AddAndroidLongExtra(List<string> arguments, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        Add(arguments, "--el", name, value);
    }

    private static void AddAndroidBooleanExtra(List<string> arguments, string name, bool value)
    {
        if (value)
        {
            Add(arguments, "--ez", name, "true");
        }
    }

    private static void AddJsonInputsAsArgs(List<string> arguments, string? inputsJson)
    {
        if (string.IsNullOrWhiteSpace(inputsJson))
        {
            return;
        }

        using var document = JsonDocument.Parse(inputsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("api.plan inputs must be a JSON object.");
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.GetRawText();
            Add(arguments, "--arg", $"{property.Name}={value}");
        }
    }

    private static string FormatCommand(string executable, IReadOnlyList<string> arguments)
        => string.Join(" ", new[] { executable }.Concat(arguments).Select(Quote));

    private static string Quote(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        if (!value.Any(static character => char.IsWhiteSpace(character) || character is '\'' or '"' or '`'))
        {
            return value;
        }

        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private sealed record CompanionCommandPlan(
        string Executable,
        IReadOnlyList<string> Arguments);
}

public sealed record CompanionOperationPlan(
    string SchemaVersion,
    string OperationId,
    string McpToolName,
    string Owner,
    string Safety,
    bool Allowed,
    bool RequiresSideEffectOptIn,
    string GateReason,
    string Executable,
    IReadOnlyList<string> Arguments,
    string DisplayCommand,
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyList<string> Warnings,
    string? AndroidIntentAction,
    string? AndroidIntentComponent,
    JsonElement? KioskCommandRunRecordTemplate);
