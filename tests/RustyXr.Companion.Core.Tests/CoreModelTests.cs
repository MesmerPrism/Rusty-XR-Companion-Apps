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
}
