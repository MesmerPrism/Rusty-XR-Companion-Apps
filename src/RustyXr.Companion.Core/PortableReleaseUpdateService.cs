using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RustyXr.Companion.Core;

public sealed record PortableReleaseUpdateStatus(
    bool IsApplicable,
    bool UpdateAvailable,
    string CurrentVersion,
    string? AvailableVersion,
    string? AppZipDownloadUrl,
    string ReleasePageUrl,
    string Detail);

public sealed record PortableReleaseUpdateLaunch(
    string ScriptPath,
    string ZipPath,
    string InstallRoot);

public sealed class PortableReleaseUpdateService
{
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/MesmerPrism/Rusty-XR-Companion-Apps/releases/latest";
    public const string ReleasePageUrl = "https://github.com/MesmerPrism/Rusty-XR-Companion-Apps/releases";
    public const string AppZipAssetName = "RustyXrCompanion-win-x64.zip";
    public const string AppExeName = "RustyXr.Companion.App.exe";

    private readonly HttpClient _httpClient;

    public PortableReleaseUpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RustyXrCompanion", "1.0"));
        }
    }

    public async Task<PortableReleaseUpdateStatus> CheckAsync(
        AppBuildIdentity identity,
        CancellationToken cancellationToken = default)
    {
        if (!identity.AutoUpdatesEnabled)
        {
            return new PortableReleaseUpdateStatus(
                IsApplicable: false,
                UpdateAvailable: false,
                identity.CurrentVersion,
                AvailableVersion: null,
                AppZipDownloadUrl: null,
                ReleasePageUrl,
                "Automatic release updates only run from the published release install root.");
        }

        using var response = await _httpClient.GetAsync(LatestReleaseApiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var availableVersion = AppBuildIdentity.NormalizeVersion(root.GetProperty("tag_name").GetString());
        var releasePage = root.TryGetProperty("html_url", out var htmlUrl)
            ? htmlUrl.GetString() ?? ReleasePageUrl
            : ReleasePageUrl;
        var zipUrl = TryFindAssetDownloadUrl(root, AppZipAssetName);

        if (string.IsNullOrWhiteSpace(zipUrl))
        {
            return new PortableReleaseUpdateStatus(
                IsApplicable: true,
                UpdateAvailable: false,
                identity.CurrentVersion,
                availableVersion,
                AppZipDownloadUrl: null,
                releasePage,
                $"Latest release {availableVersion} does not include {AppZipAssetName}.");
        }

        var updateAvailable = CompareVersions(availableVersion, identity.CurrentVersion) > 0;
        return new PortableReleaseUpdateStatus(
            IsApplicable: true,
            updateAvailable,
            identity.CurrentVersion,
            availableVersion,
            zipUrl,
            releasePage,
            updateAvailable
                ? $"Release {availableVersion} is newer than installed {identity.CurrentVersion}."
                : $"Installed release {identity.CurrentVersion} is current.");
    }

    public async Task<PortableReleaseUpdateLaunch> StartUpdateAsync(
        AppBuildIdentity identity,
        PortableReleaseUpdateStatus status,
        int processId,
        CancellationToken cancellationToken = default)
    {
        if (identity.Channel != AppInstallChannel.Release || string.IsNullOrWhiteSpace(identity.InstallRoot))
        {
            throw new InvalidOperationException("Portable release updates can only replace the published release install.");
        }

        if (!status.UpdateAvailable || string.IsNullOrWhiteSpace(status.AppZipDownloadUrl))
        {
            throw new InvalidOperationException("No portable release update is available.");
        }

        var updateRoot = Path.Combine(Path.GetTempPath(), "RustyXrCompanionUpdate", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(updateRoot);
        var zipPath = Path.Combine(updateRoot, AppZipAssetName);
        var scriptPath = Path.Combine(updateRoot, "Update-RustyXrCompanion.ps1");

        await using (var output = File.Create(zipPath))
        using (var response = await _httpClient.GetAsync(status.AppZipDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await response.Content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        WriteUpdaterScript(scriptPath);
        var arguments =
            $"-NoProfile -ExecutionPolicy Bypass -File {Quote(scriptPath)} " +
            $"-WaitPid {processId.ToString(CultureInfo.InvariantCulture)} " +
            $"-ZipPath {Quote(zipPath)} " +
            $"-InstallRoot {Quote(identity.InstallRoot)} " +
            $"-ExeName {Quote(AppExeName)}";

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        return new PortableReleaseUpdateLaunch(scriptPath, zipPath, identity.InstallRoot);
    }

    public static int CompareVersions(string? left, string? right)
    {
        var leftParts = ParseVersionParts(AppBuildIdentity.NormalizeVersion(left));
        var rightParts = ParseVersionParts(AppBuildIdentity.NormalizeVersion(right));
        var count = Math.Max(leftParts.Count, rightParts.Count);

        for (var index = 0; index < count; index++)
        {
            var leftValue = index < leftParts.Count ? leftParts[index] : 0;
            var rightValue = index < rightParts.Count ? rightParts[index] : 0;
            var comparison = leftValue.CompareTo(rightValue);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    internal static string? TryFindAssetDownloadUrl(JsonElement releaseRoot, string assetName)
    {
        if (!releaseRoot.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            if (!string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;
        }

        return null;
    }

    private static IReadOnlyList<int> ParseVersionParts(string value)
    {
        var core = value.Split('-', 2)[0];
        return core.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0)
            .ToArray();
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static void WriteUpdaterScript(string path)
    {
        File.WriteAllText(path, """
            [CmdletBinding()]
            param(
                [Parameter(Mandatory = $true)]
                [int]$WaitPid,
                [Parameter(Mandatory = $true)]
                [string]$ZipPath,
                [Parameter(Mandatory = $true)]
                [string]$InstallRoot,
                [Parameter(Mandatory = $true)]
                [string]$ExeName
            )

            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'

            $expectedRoot = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\RustyXrCompanion')).TrimEnd('\')
            $resolvedRoot = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
            if (-not [string]::Equals($expectedRoot, $resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to update unexpected install root: $resolvedRoot"
            }

            try {
                Wait-Process -Id $WaitPid -Timeout 30 -ErrorAction SilentlyContinue
            }
            catch {
            }

            $staging = "$resolvedRoot.staging"
            if (Test-Path -LiteralPath $staging) {
                Remove-Item -LiteralPath $staging -Recurse -Force
            }

            New-Item -ItemType Directory -Force -Path $staging | Out-Null
            Expand-Archive -LiteralPath $ZipPath -DestinationPath $staging -Force

            if (-not (Test-Path -LiteralPath (Join-Path $staging $ExeName))) {
                throw "Downloaded release zip did not contain $ExeName"
            }

            New-Item -ItemType Directory -Force -Path $resolvedRoot | Out-Null
            Get-ChildItem -LiteralPath $resolvedRoot -Force | Remove-Item -Recurse -Force
            Copy-Item -Path (Join-Path $staging '*') -Destination $resolvedRoot -Recurse -Force
            Remove-Item -LiteralPath $staging -Recurse -Force

            $exePath = Join-Path $resolvedRoot $ExeName
            Start-Process -FilePath $exePath -WorkingDirectory $resolvedRoot
            """);
    }
}
