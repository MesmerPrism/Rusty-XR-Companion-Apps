using System.Reflection;

namespace RustyXr.Companion.Core;

public enum AppInstallChannel
{
    Source,
    Dev,
    Release
}

public sealed record AppBuildIdentity(
    AppInstallChannel Channel,
    string ChannelLabel,
    string BaseDirectory,
    string? InstallRoot,
    string CurrentVersion,
    bool AutoUpdatesEnabled)
{
    public string DisplayLabel => $"{ChannelLabel} {CurrentVersion}";

    public static AppBuildIdentity Detect(
        string? baseDirectory = null,
        string? localAppData = null,
        string? currentVersion = null)
    {
        baseDirectory = NormalizeDirectory(baseDirectory ?? AppContext.BaseDirectory);
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var releaseRoot = NormalizeDirectory(Path.Combine(localAppData, "Programs", "RustyXrCompanion"));
        var devRoot = NormalizeDirectory(Path.Combine(localAppData, "Programs", "RustyXrCompanionDev"));
        var normalizedVersion = NormalizeVersion(currentVersion ?? ReadEntryAssemblyVersion());

        if (IsSameOrChild(baseDirectory, releaseRoot))
        {
            return new AppBuildIdentity(
                AppInstallChannel.Release,
                "Published release",
                baseDirectory,
                releaseRoot,
                normalizedVersion,
                AutoUpdatesEnabled: true);
        }

        if (IsSameOrChild(baseDirectory, devRoot))
        {
            return new AppBuildIdentity(
                AppInstallChannel.Dev,
                "Dev install",
                baseDirectory,
                devRoot,
                normalizedVersion,
                AutoUpdatesEnabled: false);
        }

        return new AppBuildIdentity(
            AppInstallChannel.Source,
            "Source/dev run",
            baseDirectory,
            null,
            normalizedVersion,
            AutoUpdatesEnabled: false);
    }

    internal static bool IsSameOrChild(string candidate, string root)
    {
        var normalizedCandidate = NormalizeDirectory(candidate);
        var normalizedRoot = NormalizeDirectory(root);
        return string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "0.0.0";
        }

        var trimmed = value.Trim();
        var metadataIndex = trimmed.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            trimmed = trimmed[..metadataIndex];
        }

        return trimmed.TrimStart('v', 'V');
    }

    private static string NormalizeDirectory(string path)
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string ReadEntryAssemblyVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppBuildIdentity).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            assembly.GetName().Version?.ToString() ??
            "0.0.0";
    }
}
