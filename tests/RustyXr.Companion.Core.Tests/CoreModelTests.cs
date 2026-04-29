using System.Security.Cryptography;
using System.Text;
using RustyXr.Companion.Core;

namespace RustyXr.Companion.Core.Tests;

public sealed class CoreModelTests
{
    [Theory]
    [InlineData("192.168.1.25", "192.168.1.25", 5555)]
    [InlineData("192.168.1.25:5556", "192.168.1.25", 5556)]
    public void QuestEndpointParsesHostAndPort(string raw, string host, int port)
    {
        Assert.True(QuestEndpoint.TryParse(raw, out var endpoint));
        Assert.Equal(host, endpoint.Host);
        Assert.Equal(port, endpoint.Port);
    }

    [Fact]
    public void QuestEndpointRejectsInvalidPort()
    {
        Assert.False(QuestEndpoint.TryParse("192.168.1.25:not-a-port", out _));
    }

    [Fact]
    public void CommandResultCondensesOutput()
    {
        var result = new CommandResult("tool", "arg", 0, new string('a', 900), string.Empty, TimeSpan.Zero);
        Assert.EndsWith("...", result.CondensedOutput);
        Assert.True(result.CondensedOutput.Length <= 800);
    }

    [Fact]
    public void QuestSessionCatalogExposesRustyXrSchemaVersion()
    {
        var catalog = new QuestSessionCatalog(
            QuestSessionCatalog.CurrentSchemaVersion,
            Array.Empty<QuestAppTarget>(),
            Array.Empty<DeviceProfile>(),
            Array.Empty<RuntimeProfile>());

        Assert.Equal("rusty.xr.quest-app-catalog.v1", catalog.SchemaVersion);
    }

    [Fact]
    public async Task CatalogLoaderBackfillsMissingSchemaVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rusty-xr-catalog-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """{"apps":[],"deviceProfiles":[],"runtimeProfiles":[]}""");

        try
        {
            var catalog = await new CatalogLoader().LoadAsync(path);

            Assert.Equal(QuestSessionCatalog.CurrentSchemaVersion, catalog.SchemaVersion);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CatalogLoaderSelectsAndResolvesRelativeApkPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rusty-xr-catalog-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "catalogs", "apps.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """
            {
              "schemaVersion": "rusty.xr.quest-app-catalog.v1",
              "apps": [
                {
                  "id": "example",
                  "label": "Example",
                  "packageName": "com.example.questapp",
                  "activityName": ".MainActivity",
                  "apkFile": "../build/example.apk",
                  "description": "Example target."
                }
              ],
              "deviceProfiles": [],
              "runtimeProfiles": []
            }
            """);

        try
        {
            var selection = await new CatalogLoader().SelectAppAsync(path, "example");

            Assert.Equal("com.example.questapp", selection.App.PackageName);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "catalogs", "..", "build", "example.apk")), selection.ResolvedApkPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AppBuildIdentityDetectsReleaseAndDevInstallRoots()
    {
        var localAppData = Path.Combine(Path.GetTempPath(), $"rusty-xr-appdata-{Guid.NewGuid():N}");
        var releaseBase = Path.Combine(localAppData, "Programs", "RustyXrCompanion");
        var devBase = Path.Combine(localAppData, "Programs", "RustyXrCompanionDev");

        var release = AppBuildIdentity.Detect(releaseBase, localAppData, "v1.2.3+build");
        var dev = AppBuildIdentity.Detect(devBase, localAppData, "0.1.0-dev");
        var source = AppBuildIdentity.Detect(Path.Combine(localAppData, "repo", "bin"), localAppData, "0.1.0-local");

        Assert.Equal(AppInstallChannel.Release, release.Channel);
        Assert.True(release.AutoUpdatesEnabled);
        Assert.Equal("1.2.3", release.CurrentVersion);
        Assert.Equal(AppInstallChannel.Dev, dev.Channel);
        Assert.False(dev.AutoUpdatesEnabled);
        Assert.Equal(AppInstallChannel.Source, source.Channel);
        Assert.False(source.AutoUpdatesEnabled);
    }

    [Theory]
    [InlineData("1.2.1", "1.2.0", 1)]
    [InlineData("v1.2.0", "1.2.0.0", 0)]
    [InlineData("1.2.0", "1.2.1", -1)]
    public void PortableReleaseUpdateServiceComparesReleaseVersions(string left, string right, int expectedSign)
    {
        var comparison = PortableReleaseUpdateService.CompareVersions(left, right);

        Assert.Equal(Math.Sign(expectedSign), Math.Sign(comparison));
    }

    [Fact]
    public async Task PortableReleaseUpdateServiceIgnoresSourceChannel()
    {
        var identity = new AppBuildIdentity(
            AppInstallChannel.Source,
            "Source/dev run",
            Path.GetTempPath(),
            null,
            "0.1.0-local",
            AutoUpdatesEnabled: false);

        var status = await new PortableReleaseUpdateService().CheckAsync(identity);

        Assert.False(status.IsApplicable);
        Assert.False(status.UpdateAvailable);
    }

    [Theory]
    [InlineData("192.168.4.1 dev wlan0 proto kernel scope link src 192.168.4.42", "192.168.4.42")]
    [InlineData("3: wlan0: <UP> mtu 1500\r\n    inet 10.0.0.24/24 brd 10.0.0.255 scope global wlan0", "10.0.0.24")]
    public void QuestAdbServiceParsesWifiIpAddress(string output, string expectedIp)
    {
        Assert.Equal(expectedIp, QuestAdbService.TryParseWifiIpAddress(output));
    }

    [Fact]
    public void HzdbServiceParsesKeepAwakeProximityStatus()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var raw = """
            VR Power Manager State:
              State: HEADSET_MOUNTED
              isAutosleepDisabled: true
              AutoSleepTime: 300000 ms
              Virtual proximity state: CLOSE
                1.0s (2.0s ago) - received com.oculus.vrpowermanager.prox_close broadcast: duration=60000
            """;

        Assert.True(HzdbService.TryParseQuestProximityStatus(raw, observedAt, out var status));
        Assert.True(status.Available);
        Assert.True(status.HoldActive);
        Assert.Equal("CLOSE", status.VirtualState);
        Assert.True(status.IsAutosleepDisabled);
        Assert.Equal(300000, status.AutoSleepTimeMs);
        Assert.NotNull(status.HoldUntil);
        Assert.Contains("Keep-awake", status.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HzdbServiceParsesNormalProximityStatus()
    {
        var raw = """
            VR Power Manager State:
              State: IDLE
              isAutosleepDisabled: false
              Virtual proximity state: DISABLED
            """;

        Assert.True(HzdbService.TryParseQuestProximityStatus(raw, DateTimeOffset.UtcNow, out var status));
        Assert.False(status.HoldActive);
        Assert.Equal("DISABLED", status.VirtualState);
        Assert.Contains("Normal proximity", status.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OfficialQuestToolingServiceVerifiesChecksums()
    {
        var payload = Encoding.UTF8.GetBytes("rusty-xr-tooling");
        var sha512Integrity = "sha512-" + Convert.ToBase64String(SHA512.HashData(payload));
        var sha1 = Convert.ToHexString(SHA1.HashData(payload)).ToLowerInvariant();
        var sha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        Assert.True(OfficialQuestToolingService.IntegrityMatchesSha512(payload, sha512Integrity));
        Assert.True(OfficialQuestToolingService.ChecksumMatchesSha1(payload, sha1));
        Assert.True(OfficialQuestToolingService.ChecksumMatchesSha256(payload, "sha256:" + sha256));
        Assert.False(OfficialQuestToolingService.ChecksumMatchesSha256(payload, new string('0', 64)));
    }

    [Fact]
    public void OfficialQuestToolingServiceParsesReleaseMetadataHelpers()
    {
        Assert.Equal("37.0.0", OfficialQuestToolingService.ParsePlatformToolsRevision("Pkg.Revision = 37.0.0"));
        Assert.Equal(
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            OfficialQuestToolingService.ParseSha256SumsFile(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa *scrcpy-win64-v1.0.zip",
                "scrcpy-win64-v1.0.zip"));
        Assert.True(OfficialQuestToolingService.NeedsInstall(null, "missing.exe", "1.0", _ => false));
        Assert.False(OfficialQuestToolingService.NeedsInstall("1.0", "tool.exe", "1.0", _ => true));
    }
}
