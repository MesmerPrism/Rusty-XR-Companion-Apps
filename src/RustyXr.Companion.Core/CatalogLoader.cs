using System.Text.Json;

namespace RustyXr.Companion.Core;

public sealed class CatalogLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<QuestSessionCatalog> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var catalog = await JsonSerializer.DeserializeAsync<QuestSessionCatalog>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return catalog ?? new QuestSessionCatalog(Array.Empty<QuestAppTarget>(), Array.Empty<DeviceProfile>(), Array.Empty<RuntimeProfile>());
    }

    public async Task SaveExampleAsync(string path, CancellationToken cancellationToken = default)
    {
        var catalog = new QuestSessionCatalog(
            new[]
            {
                new QuestAppTarget(
                    "example-user-apk",
                    "User supplied Quest APK",
                    "com.example.questapp",
                    null,
                    null,
                    "Placeholder target. Replace the package name or select an APK from the app.")
            },
            new[]
            {
                new DeviceProfile(
                    "balanced-dev",
                    "Balanced development",
                    new[]
                    {
                        new DeviceProperty("debug.oculus.cpuLevel", "2"),
                        new DeviceProperty("debug.oculus.gpuLevel", "2")
                    },
                    "Generic development profile for moderate thermal load.")
            },
            new[]
            {
                new RuntimeProfile(
                    "example-runtime",
                    "Example runtime profile",
                    new Dictionary<string, string>
                    {
                        ["example.enabled"] = "true"
                    },
                    "Placeholder profile for apps that consume a target-specific config file.")
            });

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, catalog, JsonOptions, cancellationToken).ConfigureAwait(false);
    }
}
