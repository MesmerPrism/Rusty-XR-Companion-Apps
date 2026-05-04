using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RustyXr.Companion.Core;

public sealed record ManagedMediaToolingProgress(string Status, string Detail, int PercentComplete);

public sealed record ManagedMediaToolStatus(
    string Id,
    string DisplayName,
    bool IsInstalled,
    string? InstalledVersion,
    string? AvailableVersion,
    bool UpdateAvailable,
    string InstallPath,
    string SourceUri,
    string LicenseSummary,
    string LicenseUri);

public sealed record ManagedMediaToolingStatus(ManagedMediaToolStatus Ffmpeg)
{
    public bool IsMediaRuntimeReady => Ffmpeg.IsInstalled;
    public bool HasUpdates => Ffmpeg.UpdateAvailable;
}

public sealed record ManagedMediaToolingInstallResult(
    ManagedMediaToolingStatus Status,
    bool Changed,
    string Summary,
    string Detail);

public sealed record FfmpegRuntimeClassification(
    string LicenseClass,
    bool ApprovedForDefaultUse,
    bool EnableGpl,
    bool EnableNonfree,
    string Detail);

public static class FfmpegRuntimeClassifier
{
    public static FfmpegRuntimeClassification ClassifyVersionOutput(string versionOutput)
    {
        var hasConfigurationLine = versionOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(static line => line.StartsWith("configuration:", StringComparison.OrdinalIgnoreCase));
        var configureFlags = ParseConfigureFlags(versionOutput);
        if (!hasConfigurationLine)
        {
            return new FfmpegRuntimeClassification(
                "unknown",
                false,
                EnableGpl: false,
                EnableNonfree: false,
                "Available, but FFmpeg configure flags could not be read for default media-runtime classification.");
        }

        var enableGpl = configureFlags.Contains("--enable-gpl", StringComparer.OrdinalIgnoreCase);
        var enableNonfree = configureFlags.Contains("--enable-nonfree", StringComparer.OrdinalIgnoreCase);

        if (enableNonfree)
        {
            return new FfmpegRuntimeClassification(
                "nonfree",
                false,
                enableGpl,
                true,
                "Available, but rejected for default media use because --enable-nonfree is present.");
        }

        if (enableGpl)
        {
            return new FfmpegRuntimeClassification(
                "GPL",
                false,
                true,
                enableNonfree,
                "Available as an advanced/user-supplied runtime; default media use is not approved because --enable-gpl is present.");
        }

        return new FfmpegRuntimeClassification(
            "LGPL-compatible",
            true,
            false,
            false,
            "Available. FFmpeg configure flags do not include --enable-gpl or --enable-nonfree.");
    }

    public static IReadOnlySet<string> ParseConfigureFlags(string versionOutput)
    {
        var configurationLine = versionOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(static line => line.StartsWith("configuration:", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(configurationLine))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var separatorIndex = configurationLine.IndexOf(':');
        if (separatorIndex < 0 || separatorIndex >= configurationLine.Length - 1)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return configurationLine[(separatorIndex + 1)..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static flag => flag.StartsWith("--", StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed record ManagedMediaToolMetadata(
    string Version,
    string SourceUri,
    string LicenseSummary,
    string LicenseUri,
    string AssetName,
    string Sha256);

public static class ManagedMediaToolingLayout
{
    public static string RootPath => OfficialQuestToolingLayout.RootPath;

    public static string FfmpegRootPath => Path.Combine(RootPath, "ffmpeg");
    public static string FfmpegCurrentPath => Path.Combine(FfmpegRootPath, "current");
    public static string FfmpegBinPath => Path.Combine(FfmpegCurrentPath, "bin");
    public static string FfmpegExecutablePath => Path.Combine(FfmpegBinPath, "ffmpeg.exe");
    public static string FfprobeExecutablePath => Path.Combine(FfmpegBinPath, "ffprobe.exe");
    public static string FfmpegMetadataPath => Path.Combine(FfmpegCurrentPath, "metadata.json");

    public static string? TryReadInstalledFfmpegVersion()
    {
        if (!File.Exists(FfmpegMetadataPath))
        {
            return null;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<ManagedMediaToolMetadata>(File.ReadAllText(FfmpegMetadataPath));
            return string.IsNullOrWhiteSpace(metadata?.Version) ? null : metadata.Version.Trim();
        }
        catch
        {
            return null;
        }
    }
}

public sealed class ManagedMediaToolingService : IDisposable
{
    public const string BtbNFfmpegLatestReleaseApiUri = "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest";
    public const string BtbNFfmpegProjectUri = "https://github.com/BtbN/FFmpeg-Builds";
    public const string FfmpegLicenseSummary = "FFmpeg Windows x64 LGPL shared build";
    public const string FfmpegLicenseUri = "https://ffmpeg.org/legal.html";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions WebJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;

    public ManagedMediaToolingService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = httpClient is null;

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("RustyXrCompanion/1.0");
        }
    }

    public ManagedMediaToolingStatus GetLocalStatus()
        => new(BuildFfmpegStatus(ManagedMediaToolingLayout.TryReadInstalledFfmpegVersion(), availableVersion: null));

    public async Task<ManagedMediaToolingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var release = await FetchFfmpegReleaseAsync(cancellationToken).ConfigureAwait(false);
        return new ManagedMediaToolingStatus(
            BuildFfmpegStatus(ManagedMediaToolingLayout.TryReadInstalledFfmpegVersion(), release.Version));
    }

    public async Task<ManagedMediaToolingInstallResult> InstallOrUpdateAsync(
        IProgress<ManagedMediaToolingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new ManagedMediaToolingProgress(
            "Checking FFmpeg release",
            "Reading the latest Windows LGPL shared FFmpeg build metadata from BtbN/FFmpeg-Builds.",
            10));
        var release = await FetchFfmpegReleaseAsync(cancellationToken).ConfigureAwait(false);

        progress?.Report(new ManagedMediaToolingProgress(
            "Installing FFmpeg media runtime",
            $"Downloading {release.AssetName}.",
            35));
        var changed = await EnsureFfmpegAsync(release, cancellationToken).ConfigureAwait(false);

        var status = new ManagedMediaToolingStatus(
            BuildFfmpegStatus(ManagedMediaToolingLayout.TryReadInstalledFfmpegVersion(), release.Version));

        progress?.Report(new ManagedMediaToolingProgress(
            "Managed media runtime ready",
            "FFmpeg and FFprobe are available from the companion LocalAppData tooling cache.",
            100));

        return new ManagedMediaToolingInstallResult(
            status,
            changed,
            changed ? "Managed FFmpeg media runtime installed or updated." : "Managed FFmpeg media runtime was already current.",
            $"FFmpeg {status.Ffmpeg.InstalledVersion ?? "n/a"}");
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    public static (string Version, string AssetName, string DownloadUri, string? ChecksumSha256, string? Sha256SumsUri, string HtmlUri)
        ParseFfmpegReleaseMetadataJson(string json)
    {
        var release = JsonSerializer.Deserialize<GithubReleaseResponse>(json, WebJsonOptions)
                      ?? throw new InvalidOperationException("FFmpeg release metadata response was empty.");

        if (string.IsNullOrWhiteSpace(release.HtmlUrl) ||
            release.Assets is null ||
            release.Assets.Count == 0)
        {
            throw new InvalidOperationException("FFmpeg release metadata response did not include the expected release URL or asset list.");
        }

        var windowsAsset = PickPreferredFfmpegAsset(release.Assets)
                           ?? throw new InvalidOperationException("FFmpeg release metadata did not include a Windows x64 LGPL shared asset.");

        if (string.IsNullOrWhiteSpace(windowsAsset.Name) ||
            string.IsNullOrWhiteSpace(windowsAsset.BrowserDownloadUrl))
        {
            throw new InvalidOperationException("FFmpeg release metadata did not include a usable Windows asset URL.");
        }

        var sha256SumsAsset = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, "checksums.sha256", StringComparison.OrdinalIgnoreCase));
        var checksum = NormalizeSha256Digest(windowsAsset.Digest);

        return (
            BuildFfmpegVersion(windowsAsset.Name.Trim(), checksum),
            windowsAsset.Name.Trim(),
            windowsAsset.BrowserDownloadUrl.Trim(),
            checksum,
            sha256SumsAsset?.BrowserDownloadUrl?.Trim(),
            release.HtmlUrl.Trim());
    }

    public static string? ParseSha256SumsFile(string sha256SumsText, string assetName)
    {
        if (string.IsNullOrWhiteSpace(sha256SumsText) || string.IsNullOrWhiteSpace(assetName))
        {
            return null;
        }

        foreach (var line in sha256SumsText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var candidateAssetName = parts[^1].TrimStart('*');
            if (!string.Equals(candidateAssetName, assetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hash = parts[0].Trim();
            return string.IsNullOrWhiteSpace(hash) ? null : hash;
        }

        return null;
    }

    private ManagedMediaToolStatus BuildFfmpegStatus(string? installedVersion, string? availableVersion)
        => new(
            Id: "ffmpeg",
            DisplayName: "FFmpeg media runtime",
            IsInstalled: File.Exists(ManagedMediaToolingLayout.FfmpegExecutablePath)
                         && File.Exists(ManagedMediaToolingLayout.FfprobeExecutablePath),
            InstalledVersion: installedVersion,
            AvailableVersion: availableVersion,
            UpdateAvailable: !string.IsNullOrWhiteSpace(availableVersion)
                             && !string.Equals(installedVersion?.Trim(), availableVersion.Trim(), StringComparison.OrdinalIgnoreCase),
            InstallPath: ManagedMediaToolingLayout.FfmpegExecutablePath,
            SourceUri: BtbNFfmpegProjectUri,
            LicenseSummary: FfmpegLicenseSummary,
            LicenseUri: FfmpegLicenseUri);

    private async Task<FfmpegReleaseMetadata> FetchFfmpegReleaseAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(BtbNFfmpegLatestReleaseApiUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var release = ParseFfmpegReleaseMetadataJson(json);
        var checksumSha256 = release.ChecksumSha256;
        if (string.IsNullOrWhiteSpace(checksumSha256) && !string.IsNullOrWhiteSpace(release.Sha256SumsUri))
        {
            using var checksumResponse = await _httpClient.GetAsync(release.Sha256SumsUri, cancellationToken).ConfigureAwait(false);
            checksumResponse.EnsureSuccessStatusCode();
            var checksumText = await checksumResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            checksumSha256 = ParseSha256SumsFile(checksumText, release.AssetName);
        }

        if (string.IsNullOrWhiteSpace(checksumSha256))
        {
            throw new InvalidOperationException("FFmpeg release metadata did not include a usable SHA-256 checksum for the Windows LGPL shared asset.");
        }

        return new FfmpegReleaseMetadata(
            BuildFfmpegVersion(release.AssetName, checksumSha256),
            release.AssetName,
            release.DownloadUri,
            checksumSha256,
            release.HtmlUri);
    }

    private async Task<bool> EnsureFfmpegAsync(FfmpegReleaseMetadata release, CancellationToken cancellationToken)
    {
        var installedVersion = ManagedMediaToolingLayout.TryReadInstalledFfmpegVersion();
        if (!OfficialQuestToolingService.NeedsInstall(
                installedVersion,
                ManagedMediaToolingLayout.FfmpegExecutablePath,
                release.Version))
        {
            return false;
        }

        var payloadBytes = await DownloadBytesAsync(release.DownloadUri, cancellationToken).ConfigureAwait(false);
        if (!OfficialQuestToolingService.ChecksumMatchesSha256(payloadBytes, release.ChecksumSha256))
        {
            throw new InvalidOperationException($"FFmpeg checksum verification failed for {release.DownloadUri}.");
        }

        var componentRoot = ManagedMediaToolingLayout.FfmpegRootPath;
        var stagingPath = CreateComponentStagingPath(componentRoot);
        Directory.CreateDirectory(stagingPath);

        try
        {
            ExtractFfmpegArchive(payloadBytes, stagingPath);
            WriteMetadata(
                Path.Combine(stagingPath, "metadata.json"),
                new ManagedMediaToolMetadata(
                    release.Version,
                    release.HtmlUri,
                    FfmpegLicenseSummary,
                    FfmpegLicenseUri,
                    release.AssetName,
                    release.ChecksumSha256));
            ReplaceCurrentDirectory(componentRoot, stagingPath);
        }
        catch
        {
            TryDeleteDirectory(stagingPath);
            throw;
        }

        return true;
    }

    private async Task<byte[]> DownloadBytesAsync(string uri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ExtractFfmpegArchive(byte[] payloadBytes, string destinationPath)
    {
        using var archiveStream = new MemoryStream(payloadBytes, writable: false);
        using var zipArchive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        foreach (var entry in zipArchive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName))
            {
                continue;
            }

            var normalized = NormalizeArchivePath(entry.FullName);
            var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length <= 1)
            {
                continue;
            }

            if (segments.Any(static segment => segment is "." or ".."))
            {
                throw new InvalidOperationException("FFmpeg archive contained an unsafe relative path.");
            }

            var relativePath = Path.Combine(segments.Skip(1).ToArray());
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var targetPath = Path.Combine(destinationPath, relativePath);
            var targetFullPath = Path.GetFullPath(targetPath);
            var destinationFullPath = Path.GetFullPath(destinationPath) + Path.DirectorySeparatorChar;
            if (!targetFullPath.StartsWith(destinationFullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("FFmpeg archive extraction target escaped the staging directory.");
            }

            if (normalized.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);
        }

        if (!File.Exists(Path.Combine(destinationPath, "bin", "ffmpeg.exe")))
        {
            throw new InvalidOperationException("FFmpeg archive did not include bin/ffmpeg.exe.");
        }

        if (!File.Exists(Path.Combine(destinationPath, "bin", "ffprobe.exe")))
        {
            throw new InvalidOperationException("FFmpeg archive did not include bin/ffprobe.exe.");
        }
    }

    private static GithubReleaseAssetResponse? PickPreferredFfmpegAsset(IReadOnlyList<GithubReleaseAssetResponse> assets)
    {
        var releaseBranchAsset = assets
            .Where(static asset => IsReleaseBranchLgplSharedWindowsAsset(asset.Name))
            .OrderByDescending(static asset => ParseAssetPriorityVersion(asset.Name))
            .ThenBy(static asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (releaseBranchAsset is not null)
        {
            return releaseBranchAsset;
        }

        return assets.FirstOrDefault(static asset =>
                   string.Equals(asset.Name, "ffmpeg-master-latest-win64-lgpl-shared.zip", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(static asset =>
                   asset.Name.Contains("win64", StringComparison.OrdinalIgnoreCase)
                   && asset.Name.Contains("lgpl-shared", StringComparison.OrdinalIgnoreCase)
                   && asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsReleaseBranchLgplSharedWindowsAsset(string name)
        => Regex.IsMatch(
            name,
            @"^ffmpeg-n[^-]+-latest-win64-lgpl-shared-[0-9.]+\.zip$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Version ParseAssetPriorityVersion(string assetName)
    {
        var match = Regex.Match(
            assetName,
            @"^ffmpeg-n[^-]+-latest-win64-lgpl-shared-(?<branch>[0-9.]+)\.zip$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && Version.TryParse(match.Groups["branch"].Value, out var version)
            ? version
            : new Version(0, 0);
    }

    private static string BuildFfmpegVersion(string assetName, string? checksumSha256)
    {
        var channel = "latest";
        var releaseBranchMatch = Regex.Match(
            assetName,
            @"^ffmpeg-n[^-]+-latest-win64-lgpl-shared-(?<branch>[0-9.]+)\.zip$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (releaseBranchMatch.Success)
        {
            channel = releaseBranchMatch.Groups["branch"].Value.Trim();
        }
        else if (assetName.Contains("master-latest", StringComparison.OrdinalIgnoreCase))
        {
            channel = "master";
        }

        if (string.IsNullOrWhiteSpace(checksumSha256))
        {
            return $"{channel}-latest";
        }

        var checksum = checksumSha256.Trim();
        var prefix = checksum.Length > 12 ? checksum[..12] : checksum;
        return $"{channel}-latest+sha.{prefix.ToLowerInvariant()}";
    }

    private static string CreateComponentStagingPath(string componentRoot)
        => Path.Combine(componentRoot, "_staging_" + Guid.NewGuid().ToString("N"));

    private static void ReplaceCurrentDirectory(string componentRoot, string stagingPath)
    {
        Directory.CreateDirectory(componentRoot);
        var currentPath = Path.Combine(componentRoot, "current");
        if (Directory.Exists(currentPath))
        {
            Directory.Delete(currentPath, recursive: true);
        }

        Directory.Move(stagingPath, currentPath);
    }

    private static string NormalizeArchivePath(string path)
        => path.Replace('\\', '/').TrimStart('/');

    private static string? NormalizeSha256Digest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        const string prefix = "sha256:";
        var trimmed = digest.Trim();
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..].Trim()
            : trimmed;
    }

    private static void WriteMetadata(string destinationPath, ManagedMediaToolMetadata metadata)
        => File.WriteAllText(destinationPath, JsonSerializer.Serialize(metadata, JsonOptions));

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Cleanup should not hide the install failure.
        }
    }

    private sealed record FfmpegReleaseMetadata(string Version, string AssetName, string DownloadUri, string ChecksumSha256, string HtmlUri);

    private sealed record GithubReleaseResponse(
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("assets")] IReadOnlyList<GithubReleaseAssetResponse>? Assets);

    private sealed record GithubReleaseAssetResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        [property: JsonPropertyName("digest")] string? Digest);
}
