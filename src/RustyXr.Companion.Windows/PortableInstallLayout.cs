using System.Diagnostics;
using Microsoft.Win32;

namespace RustyXr.Companion.Windows;

public readonly record struct PortableInstallProgress(string Status, string Detail, int Percent);

public sealed record PortableUninstallLaunch(
    string ScriptPath,
    string InstallRoot,
    string DataRoot,
    bool PurgeData);

public static class PortableInstallLayout
{
    public const string AppDisplayName = "Rusty XR Companion";
    public const string AppPublisher = "MesmerPrism";
    public const string AppExeName = "RustyXr.Companion.App.exe";
    public const string UninstallerFileName = "RustyXrCompanion-Uninstall.exe";
    public const string ReleaseInstallDirectoryName = "RustyXrCompanion";
    public const string DevInstallDirectoryName = "RustyXrCompanionDev";
    public const string AppDataDirectoryName = "RustyXrCompanion";
    public const string StartMenuFolderName = "Rusty XR Companion";
    public const string ReleaseShortcutName = "Rusty XR Companion.url";
    public const string UninstallRegistrySubKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\RustyXrCompanion";

    public static string ReleaseInstallRoot(string? localAppData = null)
        => Path.Combine(
            localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            ReleaseInstallDirectoryName);

    public static string DataRoot(string? localAppData = null)
        => Path.Combine(
            localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataDirectoryName);

    public static string AppExePath(string installRoot)
        => Path.Combine(installRoot, AppExeName);

    public static string UninstallerPath(string installRoot)
        => Path.Combine(installRoot, UninstallerFileName);

    public static string StartMenuDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            StartMenuFolderName);

    public static string ReleaseShortcutPath()
        => Path.Combine(StartMenuDirectory(), ReleaseShortcutName);

    public static string ReleaseShortcutDisplayPath()
        => Path.Combine(
            @"%APPDATA%\Microsoft\Windows\Start Menu\Programs",
            StartMenuFolderName,
            ReleaseShortcutName);

    public static bool IsExpectedReleaseInstallRoot(string installRoot, string? localAppData = null)
    {
        var expected = NormalizeDirectory(ReleaseInstallRoot(localAppData));
        var actual = NormalizeDirectory(installRoot);
        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsExpectedDataRoot(string dataRoot, string? localAppData = null)
    {
        var expected = NormalizeDirectory(DataRoot(localAppData));
        var actual = NormalizeDirectory(dataRoot);
        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }

    public static void AssertExpectedReleaseInstallRoot(string installRoot, string? localAppData = null)
    {
        if (!IsExpectedReleaseInstallRoot(installRoot, localAppData))
        {
            throw new InvalidOperationException($"Refusing to operate on unexpected install root: {installRoot}");
        }
    }

    public static void AssertExpectedDataRoot(string dataRoot, string? localAppData = null)
    {
        if (!IsExpectedDataRoot(dataRoot, localAppData))
        {
            throw new InvalidOperationException($"Refusing to operate on unexpected app data root: {dataRoot}");
        }
    }

    internal static string NormalizeDirectory(string path)
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

public static class PortableInstallRegistration
{
    public static void EnsureInstalledUninstaller(string installRoot, string? sourceUninstallerPath)
    {
        PortableInstallLayout.AssertExpectedReleaseInstallRoot(installRoot);
        if (string.IsNullOrWhiteSpace(sourceUninstallerPath) || !File.Exists(sourceUninstallerPath))
        {
            return;
        }

        var targetPath = PortableInstallLayout.UninstallerPath(installRoot);
        if (string.Equals(
                Path.GetFullPath(sourceUninstallerPath),
                Path.GetFullPath(targetPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(installRoot);
        File.Copy(sourceUninstallerPath, targetPath, overwrite: true);
    }

    public static void CreateReleaseShortcut(string installRoot)
    {
        PortableInstallLayout.AssertExpectedReleaseInstallRoot(installRoot);
        var shortcutDirectory = PortableInstallLayout.StartMenuDirectory();
        Directory.CreateDirectory(shortcutDirectory);

        var exePath = PortableInstallLayout.AppExePath(installRoot).Replace("\\", "/", StringComparison.Ordinal);
        var iconPath = PortableInstallLayout.AppExePath(installRoot);
        File.WriteAllText(
            PortableInstallLayout.ReleaseShortcutPath(),
            string.Join(
                Environment.NewLine,
                "[InternetShortcut]",
                $"URL=file:///{exePath}",
                $"IconFile={iconPath}",
                "IconIndex=0",
                string.Empty));
    }

    public static void RemoveReleaseShortcut()
    {
        TryDeleteFile(PortableInstallLayout.ReleaseShortcutPath());

        var shortcutDirectory = PortableInstallLayout.StartMenuDirectory();
        if (!Directory.Exists(shortcutDirectory))
        {
            return;
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(shortcutDirectory).Any())
            {
                Directory.Delete(shortcutDirectory);
            }
        }
        catch
        {
        }
    }

    public static void RegisterReleaseInstall(string installRoot, string? displayVersion = null)
    {
        PortableInstallLayout.AssertExpectedReleaseInstallRoot(installRoot);

        var appExePath = PortableInstallLayout.AppExePath(installRoot);
        var uninstallerPath = PortableInstallLayout.UninstallerPath(installRoot);
        if (!File.Exists(appExePath))
        {
            throw new FileNotFoundException("The installed app executable was not found.", appExePath);
        }

        if (!File.Exists(uninstallerPath))
        {
            throw new FileNotFoundException("The installed uninstaller was not found.", uninstallerPath);
        }

        using var key = Registry.CurrentUser.CreateSubKey(PortableInstallLayout.UninstallRegistrySubKey)
            ?? throw new InvalidOperationException("Could not create the per-user Windows uninstall entry.");
        key.SetValue("DisplayName", PortableInstallLayout.AppDisplayName, RegistryValueKind.String);
        key.SetValue("DisplayVersion", displayVersion ?? TryReadDisplayVersion(appExePath) ?? "0.0.0", RegistryValueKind.String);
        key.SetValue("Publisher", PortableInstallLayout.AppPublisher, RegistryValueKind.String);
        key.SetValue("InstallLocation", installRoot, RegistryValueKind.String);
        key.SetValue("DisplayIcon", appExePath, RegistryValueKind.String);
        key.SetValue("UninstallString", $"{Quote(uninstallerPath)} --uninstall", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"{Quote(uninstallerPath)} --quiet-uninstall", RegistryValueKind.String);
        key.SetValue("URLInfoAbout", "https://mesmerprism.github.io/Rusty-XR-Companion-Apps/", RegistryValueKind.String);
        key.SetValue("HelpLink", "https://mesmerprism.github.io/Rusty-XR-Companion-Apps/troubleshooting.html", RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", EstimateInstallSizeKilobytes(installRoot), RegistryValueKind.DWord);
    }

    public static void UnregisterReleaseInstall()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(PortableInstallLayout.UninstallRegistrySubKey, throwOnMissingSubKey: false);
        }
        catch
        {
        }
    }

    internal static string? TryReadDisplayVersion(string appExePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(appExePath);
            return !string.IsNullOrWhiteSpace(info.ProductVersion)
                ? info.ProductVersion
                : info.FileVersion;
        }
        catch
        {
            return null;
        }
    }

    internal static int EstimateInstallSizeKilobytes(string installRoot)
    {
        try
        {
            var bytes = Directory.EnumerateFiles(installRoot, "*", SearchOption.AllDirectories)
                .Sum(static file => new FileInfo(file).Length);
            return (int)Math.Min(int.MaxValue, Math.Max(1, bytes / 1024));
        }
        catch
        {
            return 1;
        }
    }

    internal static string Quote(string value)
        => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static void TryDeleteFile(string path)
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
}

public static class PortableInstallUninstaller
{
    public static PortableUninstallLaunch StartReleaseUninstall(
        bool purgeData,
        IProgress<PortableInstallProgress>? progress = null)
    {
        var installRoot = PortableInstallLayout.ReleaseInstallRoot();
        var dataRoot = PortableInstallLayout.DataRoot();
        PortableInstallLayout.AssertExpectedReleaseInstallRoot(installRoot);
        PortableInstallLayout.AssertExpectedDataRoot(dataRoot);

        progress?.Report(new PortableInstallProgress(
            "Preparing uninstall",
            "Removing Start Menu and Windows uninstall registration.",
            20));
        PortableInstallRegistration.RemoveReleaseShortcut();
        PortableInstallRegistration.UnregisterReleaseInstall();

        progress?.Report(new PortableInstallProgress(
            "Closing app",
            "Closing any running release app before file cleanup.",
            35));
        StopRunningReleaseApps(installRoot);

        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "RustyXrCompanionUninstall",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var scriptPath = Path.Combine(tempRoot, "Uninstall-RustyXrCompanion.ps1");
        WriteCleanupScript(scriptPath);

        progress?.Report(new PortableInstallProgress(
            "Removing files",
            purgeData
                ? "Release files and LocalAppData cache will be removed."
                : "Release files will be removed. Managed tool cache and diagnostics are kept.",
            70));

        var waitPid = IsCurrentProcessInsideInstallRoot(installRoot) ? Environment.ProcessId : 0;
        var arguments =
            $"-NoProfile -ExecutionPolicy Bypass -File {Quote(scriptPath)} " +
            $"-WaitPid {waitPid.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"-InstallRoot {Quote(installRoot)} " +
            $"-DataRoot {Quote(dataRoot)} " +
            $"-AppExeName {Quote(PortableInstallLayout.AppExeName)}";
        if (purgeData)
        {
            arguments += " -PurgeData";
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        progress?.Report(new PortableInstallProgress(
            "Uninstall started",
            "Cleanup will finish after this helper exits.",
            100));
        return new PortableUninstallLaunch(scriptPath, installRoot, dataRoot, purgeData);
    }

    private static void StopRunningReleaseApps(string installRoot)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(PortableInstallLayout.AppExeName)))
        {
            try
            {
                if (process.Id == Environment.ProcessId)
                {
                    continue;
                }

                var processPath = TryGetProcessPath(process);
                if (string.IsNullOrWhiteSpace(processPath) ||
                    !PortableInstallLayout.NormalizeDirectory(processPath)
                        .StartsWith(
                            PortableInstallLayout.NormalizeDirectory(installRoot) + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (process.CloseMainWindow() && process.WaitForExit(3000))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static string? TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string Quote(string value)
        => PortableInstallRegistration.Quote(value);

    private static bool IsCurrentProcessInsideInstallRoot(string installRoot)
    {
        var currentPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return false;
        }

        return PortableInstallLayout.NormalizeDirectory(currentPath)
            .StartsWith(
                PortableInstallLayout.NormalizeDirectory(installRoot) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteCleanupScript(string path)
    {
        File.WriteAllText(path, """
            [CmdletBinding()]
            param(
                [Parameter(Mandatory = $true)]
                [int]$WaitPid,
                [Parameter(Mandatory = $true)]
                [string]$InstallRoot,
                [Parameter(Mandatory = $true)]
                [string]$DataRoot,
                [Parameter(Mandatory = $true)]
                [string]$AppExeName,
                [switch]$PurgeData
            )

            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'

            $expectedRoot = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\RustyXrCompanion')).TrimEnd('\')
            $resolvedRoot = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
            if (-not [string]::Equals($expectedRoot, $resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to uninstall unexpected install root: $resolvedRoot"
            }

            $expectedDataRoot = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'RustyXrCompanion')).TrimEnd('\')
            $resolvedDataRoot = [System.IO.Path]::GetFullPath($DataRoot).TrimEnd('\')
            if (-not [string]::Equals($expectedDataRoot, $resolvedDataRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to purge unexpected data root: $resolvedDataRoot"
            }

            if ($WaitPid -gt 0) {
                try {
                    Wait-Process -Id $WaitPid -Timeout 30 -ErrorAction SilentlyContinue
                }
                catch {
                }
            }

            $shortcutDirectory = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\Rusty XR Companion'
            $releaseShortcut = Join-Path $shortcutDirectory 'Rusty XR Companion.url'
            Remove-Item -LiteralPath $releaseShortcut -Force -ErrorAction SilentlyContinue
            if ((Test-Path -LiteralPath $shortcutDirectory) -and -not (Get-ChildItem -LiteralPath $shortcutDirectory -Force -ErrorAction SilentlyContinue)) {
                Remove-Item -LiteralPath $shortcutDirectory -Force -ErrorAction SilentlyContinue
            }

            Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\RustyXrCompanion' -Recurse -Force -ErrorAction SilentlyContinue

            for ($attempt = 0; $attempt -lt 12; $attempt++) {
                try {
                    if (Test-Path -LiteralPath $resolvedRoot) {
                        Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
                    }
                    break
                }
                catch {
                    if ($attempt -eq 11) {
                        throw
                    }
                    Start-Sleep -Seconds 1
                }
            }

            if ($PurgeData -and (Test-Path -LiteralPath $resolvedDataRoot)) {
                Remove-Item -LiteralPath $resolvedDataRoot -Recurse -Force
            }
            """);
    }
}
