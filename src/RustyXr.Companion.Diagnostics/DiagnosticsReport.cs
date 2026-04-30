using System.Text.Json;
using RustyXr.Companion.Core;

namespace RustyXr.Companion.Diagnostics;

public sealed record DiagnosticsReport(
    DateTimeOffset CapturedAt,
    string MachineName,
    string UserName,
    string OSVersion,
    string DotnetVersion,
    IReadOnlyList<ToolStatus> Tools,
    IReadOnlyList<QuestDevice> Devices,
    IReadOnlyList<QuestSnapshot> Snapshots,
    IReadOnlyList<string> Notes);

public sealed class WindowsEnvironmentAnalyzer
{
    private readonly ToolLocator _toolLocator;
    private readonly QuestAdbService _adbService;

    public WindowsEnvironmentAnalyzer(ToolLocator? toolLocator = null, QuestAdbService? adbService = null)
    {
        _toolLocator = toolLocator ?? new ToolLocator();
        _adbService = adbService ?? new QuestAdbService(_toolLocator);
    }

    public async Task<DiagnosticsReport> AnalyzeAsync(bool includeSnapshots, CancellationToken cancellationToken = default)
    {
        var tools = await _toolLocator.GetToolStatusesAsync(cancellationToken).ConfigureAwait(false);
        var devices = Array.Empty<QuestDevice>() as IReadOnlyList<QuestDevice>;
        var snapshots = new List<QuestSnapshot>();
        var notes = new List<string>();

        try
        {
            devices = await _adbService.ListDevicesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            notes.Add($"ADB device scan failed: {exception.Message}");
        }

        if (includeSnapshots)
        {
            foreach (var device in devices.Where(static device => device.IsOnline))
            {
                try
                {
                    snapshots.Add(await _adbService.GetSnapshotAsync(device.Serial, cancellationToken).ConfigureAwait(false));
                }
                catch (Exception exception)
                {
                    notes.Add($"Snapshot failed for {device.Serial}: {exception.Message}");
                }
            }
        }

        return new DiagnosticsReport(
            DateTimeOffset.Now,
            Environment.MachineName,
            Environment.UserName,
            Environment.OSVersion.VersionString,
            Environment.Version.ToString(),
            tools,
            devices,
            snapshots,
            notes);
    }
}

public sealed class DiagnosticsBundleWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<string> WriteAsync(DiagnosticsReport report, string outputRoot, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(outputRoot, $"diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(folder);

        var jsonPath = Path.Combine(folder, "diagnostics.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken).ConfigureAwait(false);

        var markdownPath = Path.Combine(folder, "diagnostics.md");
        await File.WriteAllTextAsync(markdownPath, ToMarkdown(report), cancellationToken).ConfigureAwait(false);

        return folder;
    }

    public static string ToMarkdown(DiagnosticsReport report)
    {
        var lines = new List<string>
        {
            "# Rusty XR Companion Diagnostics",
            string.Empty,
            $"Captured: `{report.CapturedAt:O}`",
            $"Machine: `{report.MachineName}`",
            $"User: `{report.UserName}`",
            $"OS: `{report.OSVersion}`",
            $".NET: `{report.DotnetVersion}`",
            string.Empty,
            "## Tools",
            string.Empty,
            "| Tool | Available | Version | Path | Detail |",
            "| --- | --- | --- | --- | --- |"
        };

        lines.AddRange(report.Tools.Select(static tool =>
            $"| {tool.DisplayName} | {tool.IsAvailable} | {Escape(tool.Version)} | {Escape(tool.Path)} | {Escape(tool.Detail)} |"));

        lines.AddRange(new[] { string.Empty, "## Devices", string.Empty });
        if (report.Devices.Count == 0)
        {
            lines.Add("No Quest devices were reported by ADB.");
        }
        else
        {
            lines.Add("| Serial | State | Model | Product |");
            lines.Add("| --- | --- | --- | --- |");
            lines.AddRange(report.Devices.Select(static device =>
                $"| {Escape(device.Serial)} | {Escape(device.State)} | {Escape(device.Model)} | {Escape(device.Product)} |"));
        }

        lines.AddRange(new[] { string.Empty, "## Snapshots", string.Empty });
        if (report.Snapshots.Count == 0)
        {
            lines.Add("No live-device snapshots were captured.");
        }
        else
        {
            lines.Add("| Serial | Model | Headset Battery | Wake | Controllers | Proximity | Foreground |");
            lines.Add("| --- | --- | --- | --- | --- | --- | --- |");
            lines.AddRange(report.Snapshots.Select(static snapshot =>
                $"| {Escape(snapshot.Serial)} | {Escape(snapshot.Model)} | {Escape(snapshot.Battery)} | {Escape(FormatWake(snapshot))} | {Escape(FormatControllers(snapshot.Controllers))} | {Escape(snapshot.Proximity?.Detail ?? "not reported")} | {Escape(snapshot.Foreground)} |"));
        }

        if (report.Notes.Count > 0)
        {
            lines.AddRange(new[] { string.Empty, "## Notes", string.Empty });
            lines.AddRange(report.Notes.Select(static note => $"- {note}"));
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static string Escape(string? value) =>
        (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");

    private static string FormatWake(QuestSnapshot snapshot)
    {
        var label = snapshot.IsAwake switch
        {
            true => "awake",
            false => "asleep",
            _ => "unknown"
        };

        var parts = new[] { label, snapshot.Wakefulness, snapshot.DisplayPowerState }
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(" / ", parts);
    }

    private static string FormatControllers(IReadOnlyList<QuestControllerStatus>? controllers)
    {
        if (controllers is null || controllers.Count == 0)
        {
            return "not reported";
        }

        return string.Join(", ", controllers.Select(static controller => controller.Detail));
    }
}
