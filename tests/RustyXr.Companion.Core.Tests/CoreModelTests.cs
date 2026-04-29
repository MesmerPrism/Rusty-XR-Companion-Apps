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
}
