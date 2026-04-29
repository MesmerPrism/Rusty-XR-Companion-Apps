namespace RustyXr.Companion.Core;

public enum ToolKind
{
    Adb,
    Hzdb,
    Scrcpy
}

public sealed record ToolStatus(
    ToolKind Kind,
    string DisplayName,
    bool IsAvailable,
    string? Path,
    string? Version,
    string Detail);

public sealed record QuestEndpoint(string Host, int Port)
{
    public const int DefaultAdbPort = 5555;

    public static bool TryParse(string raw, out QuestEndpoint endpoint)
    {
        endpoint = new QuestEndpoint(string.Empty, DefaultAdbPort);
        var value = raw.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separatorIndex = value.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            endpoint = new QuestEndpoint(value, DefaultAdbPort);
            return true;
        }

        var host = value[..separatorIndex].Trim();
        var portText = value[(separatorIndex + 1)..].Trim();
        if (host.Length == 0 || !int.TryParse(portText, out var port) || port is <= 0 or > 65535)
        {
            return false;
        }

        endpoint = new QuestEndpoint(host, port);
        return true;
    }

    public override string ToString() => $"{Host}:{Port}";
}

public sealed record QuestDevice(
    string Serial,
    string State,
    string? Model = null,
    string? Product = null)
{
    public bool IsOnline => string.Equals(State, "device", StringComparison.OrdinalIgnoreCase);
    public string Label => string.IsNullOrWhiteSpace(Model) ? Serial : $"{Model} ({Serial})";
}

public sealed record QuestSnapshot(
    string Serial,
    string Model,
    string Battery,
    string Wakefulness,
    string Foreground,
    DateTimeOffset CapturedAt);

public sealed record QuestAppTarget(
    string Id,
    string Label,
    string PackageName,
    string? ActivityName,
    string? ApkFile,
    string Description);

public sealed record DeviceProperty(string Key, string Value);

public sealed record DeviceProfile(
    string Id,
    string Label,
    IReadOnlyList<DeviceProperty> Properties,
    string Description);

public sealed record RuntimeProfile(
    string Id,
    string Label,
    IReadOnlyDictionary<string, string> Values,
    string Description);

public sealed record QuestSessionCatalog(
    string SchemaVersion,
    IReadOnlyList<QuestAppTarget> Apps,
    IReadOnlyList<DeviceProfile> DeviceProfiles,
    IReadOnlyList<RuntimeProfile> RuntimeProfiles)
{
    public const string CurrentSchemaVersion = "rusty.xr.quest-app-catalog.v1";
}

public sealed record CommandResult(
    string FileName,
    string Arguments,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration)
{
    public bool Succeeded => ExitCode == 0;
    public string CondensedOutput
    {
        get
        {
            var text = string.Join(
                Environment.NewLine,
                new[] { StandardOutput.Trim(), StandardError.Trim() }.Where(static value => value.Length > 0));
            return text.Length <= 800 ? text : text[..797] + "...";
        }
    }
}

public sealed record StreamLaunchRequest(
    string Serial,
    int? MaxSize = null,
    int? BitRateMbps = null,
    bool StayAwake = true);

public sealed record StreamSession(
    string ToolPath,
    string Arguments,
    DateTimeOffset StartedAt,
    int ProcessId);
