namespace RustyXr.Companion.Core;

public sealed class ToolLocator
{
    private readonly ICommandRunner _runner;

    public ToolLocator(ICommandRunner? runner = null)
    {
        _runner = runner ?? new CommandRunner();
    }

    public async Task<IReadOnlyList<ToolStatus>> GetToolStatusesAsync(CancellationToken cancellationToken = default)
    {
        var adbPath = FindAdb();
        var hzdbPath = FindHzdb();
        var scrcpyPath = FindScrcpy();

        return new[]
        {
            await BuildToolStatusAsync(ToolKind.Adb, "Android Debug Bridge", adbPath, "version", cancellationToken).ConfigureAwait(false),
            await BuildToolStatusAsync(ToolKind.Hzdb, "Meta Quest hzdb", hzdbPath, "--version", cancellationToken).ConfigureAwait(false),
            await BuildToolStatusAsync(ToolKind.Scrcpy, "scrcpy display cast", scrcpyPath, "--version", cancellationToken).ConfigureAwait(false)
        };
    }

    public string? FindAdb()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("RUSTY_XR_ADB"),
            OfficialQuestToolingLayout.AdbExecutablePath,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"),
            Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT") is { Length: > 0 } sdkRoot ? Path.Combine(sdkRoot, "platform-tools", "adb.exe") : null,
            Environment.GetEnvironmentVariable("ANDROID_HOME") is { Length: > 0 } androidHome ? Path.Combine(androidHome, "platform-tools", "adb.exe") : null,
            FindOnPath("adb.exe")
        };

        return candidates.FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    public string? FindHzdb()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("RUSTY_XR_HZDB"),
            OfficialQuestToolingLayout.HzdbExecutablePath,
            FindOnPath("hzdb.exe"),
            FindOnPath("hzdb.cmd"),
            FindManagedTool("hzdb", "hzdb.exe"),
            FindManagedTool("hzdb", "hzdb.cmd")
        };

        return candidates.FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    public string? FindScrcpy()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("RUSTY_XR_SCRCPY"),
            OfficialQuestToolingLayout.ScrcpyExecutablePath,
            FindOnPath("scrcpy.exe"),
            FindManagedTool("scrcpy", "scrcpy.exe")
        };

        return candidates.FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private async Task<ToolStatus> BuildToolStatusAsync(
        ToolKind kind,
        string displayName,
        string? path,
        string versionArgs,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new ToolStatus(kind, displayName, false, null, null, $"{displayName} was not found.");
        }

        try
        {
            var result = await _runner.RunAsync(path, versionArgs, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            var version = FirstNonEmptyLine(result.StandardOutput) ?? FirstNonEmptyLine(result.StandardError);
            return new ToolStatus(kind, displayName, result.Succeeded, path, version, result.Succeeded ? "Available." : result.CondensedOutput);
        }
        catch (Exception exception)
        {
            return new ToolStatus(kind, displayName, false, path, null, exception.Message);
        }
    }

    private static string? FindManagedTool(string toolFolder, string executable)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RustyXrCompanion",
            "tooling",
            toolFolder);

        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(root, executable, SearchOption.AllDirectories)
            .OrderByDescending(static path => path.Length)
            .FirstOrDefault();
    }

    private static string? FindOnPath(string executable)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var entry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(entry.Trim(), executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? FirstNonEmptyLine(string value) =>
        value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .FirstOrDefault(static line => line.Length > 0);
}
