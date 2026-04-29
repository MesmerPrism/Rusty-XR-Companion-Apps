using System.Text.RegularExpressions;

namespace RustyXr.Companion.Core;

public sealed class QuestAdbService
{
    private readonly ToolLocator _toolLocator;
    private readonly ICommandRunner _runner;

    public QuestAdbService(ToolLocator? toolLocator = null, ICommandRunner? runner = null)
    {
        _runner = runner ?? new CommandRunner();
        _toolLocator = toolLocator ?? new ToolLocator(_runner);
    }

    public async Task<IReadOnlyList<QuestDevice>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var adb = RequireAdb();
        var result = await _runner.RunAsync(adb, "devices -l", TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.CondensedOutput);
        }

        return ParseDevices(result.StandardOutput);
    }

    public async Task<CommandResult> ConnectAsync(QuestEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        var adb = RequireAdb();
        return await _runner.RunAsync(adb, $"connect {endpoint}", TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
    }

    public async Task<QuestSnapshot> GetSnapshotAsync(string serial, CancellationToken cancellationToken = default)
    {
        var model = await ShellTextAsync(serial, "getprop ro.product.model", cancellationToken).ConfigureAwait(false);
        var battery = ParseBattery(await ShellTextAsync(serial, "dumpsys battery", cancellationToken).ConfigureAwait(false));
        var wakefulness = ParseWakefulness(await ShellTextAsync(serial, "dumpsys power", cancellationToken).ConfigureAwait(false));
        var foreground = ParseForeground(await ShellTextAsync(serial, "dumpsys activity activities", cancellationToken).ConfigureAwait(false));

        return new QuestSnapshot(
            serial,
            string.IsNullOrWhiteSpace(model) ? "unknown Quest device" : model.Trim(),
            battery,
            wakefulness,
            foreground,
            DateTimeOffset.Now);
    }

    public Task<CommandResult> InstallAsync(string serial, string apkPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(apkPath))
        {
            throw new FileNotFoundException("APK was not found.", apkPath);
        }

        return RunAdbAsync(serial, $"install -r \"{apkPath}\"", TimeSpan.FromMinutes(4), cancellationToken);
    }

    public Task<CommandResult> LaunchAsync(string serial, string packageName, string? activityName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            throw new ArgumentException("Package name is required.", nameof(packageName));
        }

        var command = string.IsNullOrWhiteSpace(activityName)
            ? $"shell monkey -p {ShellQuote(packageName)} -c android.intent.category.LAUNCHER 1"
            : $"shell am start -n {ShellQuote(packageName + "/" + activityName)}";

        return RunAdbAsync(serial, command, TimeSpan.FromSeconds(30), cancellationToken);
    }

    public Task<CommandResult> StopAsync(string serial, string packageName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            throw new ArgumentException("Package name is required.", nameof(packageName));
        }

        return RunAdbAsync(serial, $"shell am force-stop {ShellQuote(packageName)}", TimeSpan.FromSeconds(20), cancellationToken);
    }

    public async Task<IReadOnlyList<CommandResult>> ApplyDeviceProfileAsync(
        string serial,
        int? cpuLevel,
        int? gpuLevel,
        IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken = default)
    {
        var results = new List<CommandResult>();
        if (cpuLevel is not null)
        {
            results.Add(await SetPropertyAsync(serial, "debug.oculus.cpuLevel", cpuLevel.Value.ToString(), cancellationToken).ConfigureAwait(false));
        }

        if (gpuLevel is not null)
        {
            results.Add(await SetPropertyAsync(serial, "debug.oculus.gpuLevel", gpuLevel.Value.ToString(), cancellationToken).ConfigureAwait(false));
        }

        foreach (var property in properties)
        {
            results.Add(await SetPropertyAsync(serial, property.Key, property.Value, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    public Task<CommandResult> SetPropertyAsync(string serial, string key, string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Property key is required.", nameof(key));
        }

        return RunAdbAsync(serial, $"shell setprop {ShellQuote(key)} {ShellQuote(value)}", TimeSpan.FromSeconds(15), cancellationToken);
    }

    public Task<CommandResult> ShellAsync(string serial, string command, CancellationToken cancellationToken = default) =>
        RunAdbAsync(serial, $"shell {command}", TimeSpan.FromSeconds(30), cancellationToken);

    private async Task<string> ShellTextAsync(string serial, string command, CancellationToken cancellationToken)
    {
        var result = await ShellAsync(serial, command, cancellationToken).ConfigureAwait(false);
        return result.StandardOutput.Trim();
    }

    private Task<CommandResult> RunAdbAsync(string serial, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var adb = RequireAdb();
        var serialPart = string.IsNullOrWhiteSpace(serial) ? string.Empty : $"-s {serial} ";
        return _runner.RunAsync(adb, serialPart + arguments, timeout, cancellationToken);
    }

    private string RequireAdb()
    {
        var adb = _toolLocator.FindAdb();
        if (string.IsNullOrWhiteSpace(adb))
        {
            throw new InvalidOperationException("adb.exe was not found. Install Android Platform Tools or set RUSTY_XR_ADB.");
        }

        return adb;
    }

    private static IReadOnlyList<QuestDevice> ParseDevices(string output)
    {
        var devices = new List<QuestDevice>();
        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var parts = Regex.Split(trimmed, @"\s+");
            if (parts.Length < 2)
            {
                continue;
            }

            var model = parts.FirstOrDefault(static part => part.StartsWith("model:", StringComparison.OrdinalIgnoreCase))?.Split(':').Last();
            var product = parts.FirstOrDefault(static part => part.StartsWith("product:", StringComparison.OrdinalIgnoreCase))?.Split(':').Last();
            devices.Add(new QuestDevice(parts[0], parts[1], model, product));
        }

        return devices;
    }

    private static string ParseBattery(string output)
    {
        var level = Regex.Match(output, @"(?m)^\s*level:\s*(?<level>\d+)\s*$").Groups["level"].Value;
        var status = Regex.Match(output, @"(?m)^\s*status:\s*(?<status>.+?)\s*$").Groups["status"].Value;
        return (level, status) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => $"{level}% (status {status})",
            ({ Length: > 0 }, _) => $"{level}%",
            _ => "unknown"
        };
    }

    private static string ParseWakefulness(string output)
    {
        var wakefulness = Regex.Match(output, @"mWakefulness=(?<value>\w+)").Groups["value"].Value;
        if (wakefulness.Length > 0)
        {
            return wakefulness;
        }

        var displayState = Regex.Match(output, @"Display Power.*state=(?<value>\w+)").Groups["value"].Value;
        return displayState.Length > 0 ? displayState : "unknown";
    }

    private static string ParseForeground(string output)
    {
        var match = Regex.Match(output, @"(?m)(mResumedActivity|topResumedActivity).*?\s(?<component>[A-Za-z0-9_.]+/[A-Za-z0-9_.$]+)");
        return match.Success ? match.Groups["component"].Value : "unknown";
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}
