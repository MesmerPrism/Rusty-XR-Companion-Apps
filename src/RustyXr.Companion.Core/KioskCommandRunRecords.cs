using System.Text.Json;
using System.Text.Json.Nodes;

namespace RustyXr.Companion.Core;

public static class KioskCommandRunRecords
{
    public const string CommandEvidenceSchema = "rusty.xr.kiosk.command_evidence.v1";
    public const string CommandRunRecordSchema = "rusty.xr.kiosk.command_run_record.v1";
    public const string ControlPlaneStatusSchema = "rusty.xr.kiosk.control_plane.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static JsonElement CreateBrokerStatusRecord(
        Uri statusUri,
        JsonElement status,
        DateTimeOffset receivedAt,
        string? runId = null)
    {
        var kioskStatus = TryGetKioskStatus(status);
        var foregroundAfter = ForegroundLabel(kioskStatus);
        var clockEpochId = StringProperty(kioskStatus, "clock_epoch_id");
        var surfaceIntent = SurfaceIntent(kioskStatus);
        var issues = new JsonArray();
        if (kioskStatus is null)
        {
            issues.Add("kiosk_status_missing");
        }

        var root = new JsonObject
        {
            ["schema"] = CommandRunRecordSchema,
            ["run_id"] = string.IsNullOrWhiteSpace(runId)
                ? $"companion-broker-status-{receivedAt.ToUnixTimeMilliseconds()}"
                : runId,
            ["command_goal"] = "surface.current",
            ["surface_intent"] = surfaceIntent,
            ["primary"] = Evidence(
                provider: "Companion",
                preferredCommand: "rusty-xr-companion broker status --json",
                fallbackCommand: $"GET {statusUri.PathAndQuery}",
                foregroundBefore: null,
                foregroundAfter: foregroundAfter,
                clockEpochId: clockEpochId,
                notes: ["companion_json_report_path"]),
            ["fallback"] = Evidence(
                provider: "Broker",
                preferredCommand: $"GET {statusUri.PathAndQuery}",
                fallbackCommand: "adb shell dumpsys window",
                foregroundBefore: null,
                foregroundAfter: foregroundAfter,
                clockEpochId: clockEpochId,
                notes: ["broker_http_status_path"]),
            ["status_before"] = null,
            ["status_after"] = CloneNode(kioskStatus),
            ["outcome"] = kioskStatus is null ? "Unknown" : "Succeeded",
            ["issue_codes"] = issues,
            ["notes"] = JsonArrayOf("broker_status_probe", $"status_url={statusUri}")
        };

        return ToElement(root);
    }

    public static JsonElement? CreatePlanTemplate(string operationId, string displayCommand)
    {
        if (!string.Equals(operationId, "broker.status", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var root = new JsonObject
        {
            ["schema"] = CommandRunRecordSchema,
            ["run_id"] = "plan-broker-status",
            ["command_goal"] = "surface.current",
            ["surface_intent"] = "UnknownSurface",
            ["primary"] = Evidence(
                provider: "Companion",
                preferredCommand: displayCommand,
                fallbackCommand: "GET /status",
                foregroundBefore: null,
                foregroundAfter: null,
                clockEpochId: null,
                notes: ["api_cli_mcp_plan_template"]),
            ["fallback"] = Evidence(
                provider: "Broker",
                preferredCommand: "GET /status",
                fallbackCommand: "adb shell dumpsys window",
                foregroundBefore: null,
                foregroundAfter: null,
                clockEpochId: null,
                notes: ["fallback_evidence_path"]),
            ["status_before"] = null,
            ["status_after"] = null,
            ["outcome"] = "NotStarted",
            ["issue_codes"] = new JsonArray(),
            ["notes"] = JsonArrayOf("plan_only_no_device_command_executed")
        };

        return ToElement(root);
    }

    public static JsonElement? TryCreateFromBrokerCommand(
        BrokerCommandRequest request,
        BrokerWebSocketProbeResult result)
    {
        if (!IsKioskStatusCommand(request.Command))
        {
            return null;
        }

        var kioskStatus = TryGetKioskStatusFromMessages(result.ReceivedMessages);
        var foregroundAfter = ForegroundLabel(kioskStatus);
        var clockEpochId = StringProperty(kioskStatus, "clock_epoch_id");
        var issues = new JsonArray();
        if (kioskStatus is null)
        {
            issues.Add("kiosk_status_missing");
        }

        var root = new JsonObject
        {
            ["schema"] = CommandRunRecordSchema,
            ["run_id"] = string.IsNullOrWhiteSpace(request.RequestId)
                ? $"companion-broker-command-{result.CompletedAt.ToUnixTimeMilliseconds()}"
                : request.RequestId,
            ["command_goal"] = "surface.current",
            ["surface_intent"] = SurfaceIntent(kioskStatus),
            ["primary"] = Evidence(
                provider: "Companion",
                preferredCommand: $"rusty-xr-companion broker command --command {request.Command} --json",
                fallbackCommand: "websocket kiosk.get_status",
                foregroundBefore: null,
                foregroundAfter: foregroundAfter,
                clockEpochId: clockEpochId,
                notes: ["companion_websocket_command_path"]),
            ["fallback"] = Evidence(
                provider: "Broker",
                preferredCommand: "websocket kiosk.get_status",
                fallbackCommand: "adb shell dumpsys window",
                foregroundBefore: null,
                foregroundAfter: foregroundAfter,
                clockEpochId: clockEpochId,
                notes: ["broker_websocket_status_path"]),
            ["status_before"] = null,
            ["status_after"] = CloneNode(kioskStatus),
            ["outcome"] = result.HasAcceptedAck ? kioskStatus is null ? "Unknown" : "Succeeded" : "Failed",
            ["issue_codes"] = issues,
            ["notes"] = JsonArrayOf("broker_command_probe")
        };

        return ToElement(root);
    }

    private static bool IsKioskStatusCommand(string command) =>
        string.Equals(command, "kiosk.get_status", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(command, "status_request", StringComparison.OrdinalIgnoreCase);

    private static JsonObject Evidence(
        string provider,
        string? preferredCommand,
        string? fallbackCommand,
        string? foregroundBefore,
        string? foregroundAfter,
        string? clockEpochId,
        IReadOnlyList<string> notes)
    {
        var noteArray = new JsonArray();
        foreach (var note in notes)
        {
            if (!string.IsNullOrWhiteSpace(note))
            {
                noteArray.Add(note);
            }
        }

        return new JsonObject
        {
            ["schema"] = CommandEvidenceSchema,
            ["command_goal"] = "surface.current",
            ["provider"] = string.IsNullOrWhiteSpace(provider) ? "Unknown" : provider,
            ["preferred_command"] = StringOrNull(preferredCommand),
            ["fallback_command"] = StringOrNull(fallbackCommand),
            ["foreground_before"] = StringOrNull(foregroundBefore),
            ["foreground_after"] = StringOrNull(foregroundAfter),
            ["clock_epoch_id"] = StringOrNull(clockEpochId),
            ["notes"] = noteArray
        };
    }

    private static JsonObject? TryGetKioskStatus(JsonElement status)
    {
        if (status.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (StringProperty(status, "schema") == ControlPlaneStatusSchema)
        {
            return CloneObject(status);
        }

        if (status.TryGetProperty("rustyKiosk", out var rustyKiosk) &&
            rustyKiosk.ValueKind == JsonValueKind.Object)
        {
            return CloneObject(rustyKiosk);
        }

        if (status.TryGetProperty("status", out var nestedStatus) &&
            nestedStatus.ValueKind == JsonValueKind.Object)
        {
            return TryGetKioskStatus(nestedStatus);
        }

        if (status.TryGetProperty("result", out var result) &&
            result.ValueKind == JsonValueKind.Object)
        {
            return TryGetKioskStatus(result);
        }

        return null;
    }

    private static JsonObject? TryGetKioskStatusFromMessages(
        IReadOnlyList<BrokerWebSocketReceivedMessage> messages)
    {
        foreach (var message in messages.AsEnumerable().Reverse())
        {
            var status = TryGetKioskStatus(message.Payload);
            if (status is not null)
            {
                return status;
            }
        }

        return null;
    }

    private static JsonObject? CloneObject(JsonElement element)
    {
        var node = JsonNode.Parse(element.GetRawText()) as JsonObject;
        if (node is null)
        {
            return null;
        }

        node.Remove("latest_command_run");
        node.Remove("latestCommandRun");
        node.Remove("command_run_record");
        node.Remove("commandRunRecord");
        return node;
    }

    private static JsonNode? CloneNode(JsonObject? node) =>
        node is null ? null : JsonNode.Parse(node.ToJsonString(JsonOptions));

    private static string SurfaceIntent(JsonObject? status)
    {
        var value = StringProperty(status, "surface_intent");
        return string.IsNullOrWhiteSpace(value) ? "UnknownSurface" : value;
    }

    private static string ForegroundLabel(JsonObject? status)
    {
        var packageName = StringProperty(status, "foreground_package");
        var activity = StringProperty(status, "foreground_activity");
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(activity) ? packageName : $"{packageName}/{activity}";
    }

    private static string StringProperty(JsonObject? node, string propertyName) =>
        node is not null &&
        node.TryGetPropertyValue(propertyName, out var value) &&
        value is JsonValue jsonValue &&
        jsonValue.TryGetValue<string>(out var text)
            ? text
            : string.Empty;

    private static string StringProperty(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static JsonNode? StringOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : JsonValue.Create(value);

    private static JsonArray JsonArrayOf(params string[] values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                array.Add(value);
            }
        }

        return array;
    }

    private static JsonElement ToElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString(JsonOptions));
        return document.RootElement.Clone();
    }
}
