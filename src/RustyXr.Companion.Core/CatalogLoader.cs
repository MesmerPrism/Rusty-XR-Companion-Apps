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
        return Normalize(catalog);
    }

    public async Task SaveExampleAsync(string path, CancellationToken cancellationToken = default)
    {
        var catalog = new QuestSessionCatalog(
            QuestSessionCatalog.CurrentSchemaVersion,
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

    private static QuestSessionCatalog Normalize(QuestSessionCatalog? catalog)
    {
        if (catalog is null)
        {
            return new QuestSessionCatalog(
                QuestSessionCatalog.CurrentSchemaVersion,
                Array.Empty<QuestAppTarget>(),
                Array.Empty<DeviceProfile>(),
                Array.Empty<RuntimeProfile>());
        }

        return catalog with
        {
            SchemaVersion = string.IsNullOrWhiteSpace(catalog.SchemaVersion)
                ? QuestSessionCatalog.CurrentSchemaVersion
                : catalog.SchemaVersion
        };
    }
}
