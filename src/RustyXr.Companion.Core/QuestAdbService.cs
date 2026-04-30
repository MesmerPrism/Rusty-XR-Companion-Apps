using System.Globalization;
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

    public async Task<QuestWifiAdbResult> EnableWifiAdbAsync(
        string serial,
        int port = QuestEndpoint.DefaultAdbPort,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException("Device serial is required.", nameof(serial));
        }

        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");
        }

        var route = await ShellAsync(serial, "ip route", cancellationToken).ConfigureAwait(false);
        var ipAddress = TryParseWifiIpAddress(route.StandardOutput)
            ?? TryParseWifiIpAddress((await ShellAsync(serial, "ip addr show wlan0", cancellationToken).ConfigureAwait(false)).StandardOutput);

        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            throw new InvalidOperationException("Could not determine the selected Quest Wi-Fi IP address from ADB.");
        }

        var tcpip = await RunAdbAsync(serial, $"tcpip {port}", TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(1500), cancellationToken).ConfigureAwait(false);
        var endpoint = new QuestEndpoint(ipAddress, port);
        var connect = await ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);

        return new QuestWifiAdbResult(serial, endpoint, tcpip, connect, DateTimeOffset.Now);
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

    public Task<CommandResult> LaunchAsync(
        string serial,
        string packageName,
        string? activityName,
        IReadOnlyDictionary<string, string>? extras = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            throw new ArgumentException("Package name is required.", nameof(packageName));
        }

        var extrasPart = BuildAmStartExtras(extras);
        var command = string.IsNullOrWhiteSpace(activityName)
            ? string.IsNullOrEmpty(extrasPart)
                ? $"shell monkey -p {ShellQuote(packageName)} -c android.intent.category.LAUNCHER 1"
                : $"shell am start -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p {ShellQuote(packageName)}{extrasPart}"
            : $"shell am start -n {ShellQuote(packageName + "/" + activityName)}{extrasPart}";

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

    public Task<CommandResult> ClearLogcatAsync(string serial, CancellationToken cancellationToken = default) =>
        RunAdbAsync(serial, "logcat -c", TimeSpan.FromSeconds(15), cancellationToken);

    public async Task<string> DumpLogcatAsync(
        string serial,
        int lineCount,
        CancellationToken cancellationToken = default)
    {
        if (lineCount is <= 0 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(lineCount), "Line count must be between 1 and 100000.");
        }

        var result = await RunAdbAsync(
            serial,
            $"logcat -d -v time -t {lineCount}",
            TimeSpan.FromSeconds(30),
            cancellationToken).ConfigureAwait(false);

        return string.Join(
            Environment.NewLine,
            new[] { result.StandardOutput.TrimEnd(), result.StandardError.TrimEnd() }
                .Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    public Task<CommandResult> ReadRunAsTextFileAsync(
        string serial,
        string packageName,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            throw new ArgumentException("Package name is required.", nameof(packageName));
        }

        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("A relative app-private file path is required.", nameof(relativePath));
        }

        return RunAdbAsync(
            serial,
            $"shell run-as {ShellQuote(packageName)} cat {ShellQuote(relativePath)}",
            TimeSpan.FromSeconds(15),
            cancellationToken);
    }

    public Task<CommandResult> ReverseTcpAsync(
        string serial,
        int devicePort,
        int hostPort,
        CancellationToken cancellationToken = default)
    {
        if (devicePort is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(devicePort), "Port must be between 1 and 65535.");
        }

        if (hostPort is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(hostPort), "Port must be between 1 and 65535.");
        }

        return RunAdbAsync(
            serial,
            $"reverse tcp:{devicePort} tcp:{hostPort}",
            TimeSpan.FromSeconds(15),
            cancellationToken);
    }

    public async Task<QuestAppDiagnostics> GetAppDiagnosticsAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            throw new ArgumentException("Package name is required.", nameof(packageName));
        }

        var pidResult = await ShellAsync(serial, $"pidof {ShellQuote(packageName)}", cancellationToken).ConfigureAwait(false);
        var foreground = ParseForeground(await ShellTextAsync(serial, "dumpsys activity activities", cancellationToken).ConfigureAwait(false));
        var gfxResult = await ShellAsync(serial, $"dumpsys gfxinfo {ShellQuote(packageName)}", cancellationToken).ConfigureAwait(false);
        var memoryResult = await ShellAsync(serial, $"dumpsys meminfo {ShellQuote(packageName)}", cancellationToken).ConfigureAwait(false);
        var pid = pidResult.StandardOutput.Trim();

        return new QuestAppDiagnostics(
            packageName,
            ProcessRunning: pid.Length > 0,
            ProcessId: pid.Length > 0 ? pid : null,
            ForegroundMatchesPackage: foreground.StartsWith(packageName + "/", StringComparison.Ordinal),
            Foreground: foreground,
            GfxInfoSummary: SummarizeGfxInfo(gfxResult.StandardOutput),
            MemorySummary: SummarizeMemory(memoryResult.StandardOutput),
            CapturedAt: DateTimeOffset.Now);
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

    public static string? TryParseWifiIpAddress(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var routeMatch = Regex.Match(output, @"\bsrc\s+(?<ip>(?:\d{1,3}\.){3}\d{1,3})\b");
        if (routeMatch.Success)
        {
            return routeMatch.Groups["ip"].Value;
        }

        var wlanMatch = Regex.Match(output, @"\binet\s+(?<ip>(?:\d{1,3}\.){3}\d{1,3})/");
        return wlanMatch.Success ? wlanMatch.Groups["ip"].Value : null;
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

    private static string SummarizeGfxInfo(string output)
    {
        var interesting = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line =>
                line.StartsWith("Total frames rendered:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Janky frames:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("50th percentile:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("90th percentile:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("95th percentile:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("99th percentile:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Number Missed Vsync:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("HISTOGRAM:", StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToArray();

        return interesting.Length > 0
            ? string.Join(" | ", interesting)
            : "gfxinfo unavailable or no frame stats reported";
    }

    private static string SummarizeMemory(string output)
    {
        var total = Regex.Match(output, @"(?m)^\s*TOTAL\s+(?<value>\d+)");
        if (total.Success)
        {
            return $"TOTAL {total.Groups["value"].Value} KiB";
        }

        var nativeHeap = Regex.Match(output, @"(?m)^\s*Native Heap\s+(?<value>\d+)");
        return nativeHeap.Success
            ? $"Native Heap {nativeHeap.Groups["value"].Value} KiB"
            : "meminfo unavailable";
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static string BuildAmStartExtras(IReadOnlyDictionary<string, string>? extras)
    {
        if (extras is null || extras.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var (key, value) in extras)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (bool.TryParse(value, out var boolValue))
            {
                parts.Add($"--ez {ShellQuote(key)} {boolValue.ToString().ToLowerInvariant()}");
            }
            else if (int.TryParse(value, out var intValue))
            {
                parts.Add($"--ei {ShellQuote(key)} {intValue}");
            }
            else if (long.TryParse(value, out var longValue))
            {
                parts.Add($"--el {ShellQuote(key)} {longValue}");
            }
            else if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
            {
                parts.Add($"--ef {ShellQuote(key)} {floatValue.ToString(CultureInfo.InvariantCulture)}");
            }
            else
            {
                parts.Add($"--es {ShellQuote(key)} {ShellQuote(value)}");
            }
        }

        return parts.Count == 0 ? string.Empty : " " + string.Join(' ', parts);
    }
}
