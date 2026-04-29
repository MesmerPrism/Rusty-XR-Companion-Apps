using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RustyXr.Companion.Core;

public sealed class HzdbService
{
    private static readonly Regex VrPowerManagerVirtualStateRegex = new(@"Virtual proximity state:\s*(.+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VrPowerManagerAutosleepDisabledRegex = new(@"isAutosleepDisabled:\s*(true|false)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VrPowerManagerAutoSleepTimeRegex = new(@"AutoSleepTime:\s*(\d+)\s*ms", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VrPowerManagerHeadsetStateRegex = new(@"State:\s*(.+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VrPowerManagerBroadcastRegex = new(
        @"^\s*\d+(?:\.\d+)?s \(([\d\.]+)s ago\) - received com\.oculus\.vrpowermanager\.(prox_close|automation_disable) broadcast: duration=(\d+)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private readonly ToolLocator _toolLocator;
    private readonly QuestAdbService _adbService;
    private readonly ICommandRunner _runner;

    public HzdbService(ToolLocator? toolLocator = null, QuestAdbService? adbService = null, ICommandRunner? runner = null)
    {
        _runner = runner ?? new CommandRunner();
        _toolLocator = toolLocator ?? new ToolLocator(_runner);
        _adbService = adbService ?? new QuestAdbService(_toolLocator, _runner);
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_toolLocator.FindHzdb());

    public async Task<CommandResult> SetProximityAsync(
        string serial,
        bool enableNormalProximity,
        int? durationMs = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        var flag = enableNormalProximity ? "--enable" : "--disable";
        var durationArg = !enableNormalProximity && durationMs is > 0
            ? $" --duration-ms {durationMs.Value}"
            : string.Empty;

        return await RunHzdbAsync(
            $"device proximity --device \"{serial}\" {flag}{durationArg}",
            TimeSpan.FromSeconds(30),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<QuestProximityStatus> GetProximityStatusAsync(string serial, CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        var observedAt = DateTimeOffset.Now;
        var result = await _adbService.ShellAsync(serial, "dumpsys vrpowermanager", cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return new QuestProximityStatus(
                Available: false,
                HoldActive: false,
                VirtualState: string.Empty,
                IsAutosleepDisabled: false,
                HeadsetState: string.Empty,
                AutoSleepTimeMs: null,
                RetrievedAt: observedAt,
                HoldUntil: null,
                Detail: string.IsNullOrWhiteSpace(result.CondensedOutput)
                    ? "Quest vrpowermanager readback failed."
                    : result.CondensedOutput);
        }

        return TryParseQuestProximityStatus(result.StandardOutput, observedAt, out var status)
            ? status
            : new QuestProximityStatus(
                Available: false,
                HoldActive: false,
                VirtualState: string.Empty,
                IsAutosleepDisabled: false,
                HeadsetState: string.Empty,
                AutoSleepTimeMs: null,
                RetrievedAt: observedAt,
                HoldUntil: null,
                Detail: "Quest vrpowermanager output did not contain a recognizable virtual proximity state.");
    }

    public Task<CommandResult> WakeDeviceAsync(string serial, CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        return RunHzdbAsync($"device wake --device \"{serial}\"", TimeSpan.FromSeconds(30), cancellationToken);
    }

    public Task<CommandResult> GetDeviceInfoAsync(string serial, CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        return RunHzdbAsync($"device info --json \"{serial}\"", TimeSpan.FromSeconds(30), cancellationToken);
    }

    public async Task<QuestScreenshotCapture> CaptureScreenshotAsync(
        string serial,
        string outputPath,
        string? method = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSerial(serial);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        var normalizedMethod = string.IsNullOrWhiteSpace(method)
            ? "screencap"
            : method.Trim().Trim('"');

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (ShouldPreferAdbScreenshot(normalizedMethod))
        {
            var adbCapture = await CaptureScreenshotViaAdbAsync(serial, outputPath, cancellationToken).ConfigureAwait(false);
            if (adbCapture.Succeeded || !IsAvailable)
            {
                return adbCapture;
            }

            var hzdbFallback = await CaptureScreenshotViaHzdbAsync(serial, outputPath, "screencap", cancellationToken).ConfigureAwait(false);
            return hzdbFallback.Succeeded
                ? hzdbFallback with
                {
                    Detail = $"{adbCapture.Detail} Fell back to hzdb screenshot capture and succeeded.".Trim()
                }
                : hzdbFallback with
                {
                    Detail = $"{adbCapture.Detail}{Environment.NewLine}{hzdbFallback.Detail}".Trim()
                };
        }

        return await CaptureScreenshotViaHzdbAsync(serial, outputPath, normalizedMethod, cancellationToken).ConfigureAwait(false);
    }

    public static bool ShouldPreferAdbScreenshot(string? method)
        => string.IsNullOrWhiteSpace(method)
           || string.Equals(method.Trim().Trim('"'), "screencap", StringComparison.OrdinalIgnoreCase);

    public static bool TryParseQuestProximityStatus(
        string rawOutput,
        DateTimeOffset observedAt,
        out QuestProximityStatus status)
    {
        var virtualState = MatchValue(VrPowerManagerVirtualStateRegex, rawOutput);
        var autosleepText = MatchValue(VrPowerManagerAutosleepDisabledRegex, rawOutput);
        var headsetState = MatchValue(VrPowerManagerHeadsetStateRegex, rawOutput) ?? string.Empty;
        var autoSleepTimeText = MatchValue(VrPowerManagerAutoSleepTimeRegex, rawOutput);
        var autoSleepTimeMs = int.TryParse(autoSleepTimeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAutoSleep)
            ? parsedAutoSleep
            : (int?)null;
        var isAutosleepDisabled = bool.TryParse(autosleepText, out var parsedAutosleep) && parsedAutosleep;

        if (string.IsNullOrWhiteSpace(virtualState))
        {
            status = new QuestProximityStatus(
                Available: false,
                HoldActive: false,
                VirtualState: string.Empty,
                IsAutosleepDisabled: isAutosleepDisabled,
                HeadsetState: headsetState,
                AutoSleepTimeMs: autoSleepTimeMs,
                RetrievedAt: observedAt,
                HoldUntil: null,
                Detail: "Virtual proximity state was not reported.");
            return false;
        }

        var holdActive = string.Equals(virtualState, "CLOSE", StringComparison.OrdinalIgnoreCase);
        var holdUntil = TryParseLatestBroadcast(rawOutput) is { } broadcast &&
                        TryComputeHoldUntil(broadcast, observedAt, out var parsedHoldUntil)
            ? parsedHoldUntil
            : (DateTimeOffset?)null;
        var detail = holdActive
            ? "Keep-awake proximity override is active."
            : string.Equals(virtualState, "DISABLED", StringComparison.OrdinalIgnoreCase)
                ? "Normal proximity sensor behavior is active."
                : $"Virtual proximity state: {virtualState}.";

        status = new QuestProximityStatus(
            Available: true,
            HoldActive: holdActive,
            VirtualState: virtualState,
            IsAutosleepDisabled: isAutosleepDisabled,
            HeadsetState: headsetState,
            AutoSleepTimeMs: autoSleepTimeMs,
            RetrievedAt: observedAt,
            HoldUntil: holdUntil,
            Detail: detail);
        return true;
    }

    public static string CreateDefaultScreenshotPath(string outputRoot, string serial)
    {
        Directory.CreateDirectory(outputRoot);
        var sanitizedSerial = SanitizeFileToken(serial);
        return Path.Combine(outputRoot, $"quest-screenshot-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{sanitizedSerial}.png");
    }

    private async Task<CommandResult> RunHzdbAsync(string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var hzdb = _toolLocator.FindHzdb();
        if (string.IsNullOrWhiteSpace(hzdb))
        {
            throw new InvalidOperationException("hzdb.exe was not found. Install managed Quest tooling or set RUSTY_XR_HZDB.");
        }

        if (string.Equals(Path.GetExtension(hzdb), ".cmd", StringComparison.OrdinalIgnoreCase))
        {
            return await _runner
                .RunAsync("cmd.exe", $"/c \"\"{hzdb}\" {arguments}\"", timeout, cancellationToken)
                .ConfigureAwait(false);
        }

        return await _runner.RunAsync(hzdb, arguments, timeout, cancellationToken).ConfigureAwait(false);
    }

    private async Task<QuestScreenshotCapture> CaptureScreenshotViaHzdbAsync(
        string serial,
        string outputPath,
        string? method,
        CancellationToken cancellationToken)
    {
        var methodArg = string.IsNullOrWhiteSpace(method)
            ? string.Empty
            : $" --method \"{method}\"";
        var result = await RunHzdbAsync(
            $"capture screenshot --device \"{serial}\"{methodArg} -o \"{outputPath}\"",
            TimeSpan.FromSeconds(45),
            cancellationToken).ConfigureAwait(false);

        if (result.Succeeded && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
        {
            return new QuestScreenshotCapture(
                Succeeded: true,
                OutputPath: outputPath,
                Method: string.IsNullOrWhiteSpace(method) ? "hzdb" : $"hzdb:{method}",
                Detail: result.CondensedOutput.Length > 0 ? result.CondensedOutput : $"Screenshot captured to {outputPath}.",
                CapturedAt: DateTimeOffset.Now);
        }

        TryDelete(outputPath);
        return new QuestScreenshotCapture(
            Succeeded: false,
            OutputPath: outputPath,
            Method: string.IsNullOrWhiteSpace(method) ? "hzdb" : $"hzdb:{method}",
            Detail: string.IsNullOrWhiteSpace(result.CondensedOutput) ? "hzdb screenshot capture failed." : result.CondensedOutput,
            CapturedAt: DateTimeOffset.Now);
    }

    private async Task<QuestScreenshotCapture> CaptureScreenshotViaAdbAsync(
        string serial,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var adb = _toolLocator.FindAdb();
        if (string.IsNullOrWhiteSpace(adb))
        {
            return new QuestScreenshotCapture(
                Succeeded: false,
                OutputPath: outputPath,
                Method: "adb:screencap",
                Detail: "adb.exe was not found for raw Quest screencap capture.",
                CapturedAt: DateTimeOffset.Now);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = adb,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in new[] { "-s", serial, "exec-out", "screencap", "-p" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await process.StandardOutput.BaseStream.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stderr = (await stderrTask.ConfigureAwait(false)).Trim();
            var fileInfo = new FileInfo(outputPath);

            if (process.ExitCode == 0 && fileInfo.Exists && fileInfo.Length > 0)
            {
                return new QuestScreenshotCapture(
                    Succeeded: true,
                    OutputPath: outputPath,
                    Method: "adb:screencap",
                    Detail: $"Captured a raw Quest frame through adb exec-out screencap -p to {outputPath}.",
                    CapturedAt: DateTimeOffset.Now);
            }

            TryDelete(outputPath);
            return new QuestScreenshotCapture(
                Succeeded: false,
                OutputPath: outputPath,
                Method: "adb:screencap",
                Detail: string.IsNullOrWhiteSpace(stderr)
                    ? "adb exec-out screencap -p did not return a usable PNG."
                    : stderr,
                CapturedAt: DateTimeOffset.Now);
        }
        catch (OperationCanceledException)
        {
            TryDelete(outputPath);
            TryKill(process);
            throw;
        }
        catch (Exception exception)
        {
            TryDelete(outputPath);
            return new QuestScreenshotCapture(
                Succeeded: false,
                OutputPath: outputPath,
                Method: "adb:screencap",
                Detail: $"adb exec-out screencap -p failed: {exception.Message}",
                CapturedAt: DateTimeOffset.Now);
        }
    }

    private static void ValidateSerial(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException("Device serial is required.", nameof(serial));
        }
    }

    private static bool TryComputeHoldUntil(ProximityBroadcastInfo broadcast, DateTimeOffset observedAt, out DateTimeOffset holdUntil)
    {
        holdUntil = default;
        if (!string.Equals(broadcast.Action, "prox_close", StringComparison.OrdinalIgnoreCase) || broadcast.DurationMs <= 0)
        {
            return false;
        }

        var eventTime = observedAt - TimeSpan.FromSeconds(Math.Max(0, broadcast.AgeSeconds));
        var candidate = eventTime.AddMilliseconds(broadcast.DurationMs);
        if (candidate <= observedAt)
        {
            return false;
        }

        holdUntil = candidate;
        return true;
    }

    private static ProximityBroadcastInfo? TryParseLatestBroadcast(string rawOutput)
    {
        ProximityBroadcastInfo? latest = null;

        foreach (Match match in VrPowerManagerBroadcastRegex.Matches(rawOutput))
        {
            if (!match.Success ||
                !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ageSeconds) ||
                !int.TryParse(match.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var durationMs))
            {
                continue;
            }

            var candidate = new ProximityBroadcastInfo(
                match.Groups[2].Value.Trim(),
                ageSeconds,
                durationMs);

            if (latest is null || candidate.AgeSeconds < latest.AgeSeconds)
            {
                latest = candidate;
            }
        }

        return latest;
    }

    private static string? MatchValue(Regex regex, string input)
    {
        var match = regex.Match(input);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string SanitizeFileToken(string value)
        => new(value.Trim().Select(static ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private sealed record ProximityBroadcastInfo(string Action, double AgeSeconds, int DurationMs);
}
