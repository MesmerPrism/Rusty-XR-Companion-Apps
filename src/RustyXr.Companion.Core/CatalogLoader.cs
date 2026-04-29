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

    public async Task<CatalogAppSelection> SelectAppAsync(
        string path,
        string appId,
        CancellationToken cancellationToken = default)
    {
        var catalog = await LoadAsync(path, cancellationToken).ConfigureAwait(false);
        var app = catalog.Apps.FirstOrDefault(app => string.Equals(app.Id, appId, StringComparison.OrdinalIgnoreCase));
        if (app is null)
        {
            throw new InvalidOperationException($"Catalog app '{appId}' was not found.");
        }

        return new CatalogAppSelection(catalog, app, path, ResolveApkPath(path, app));
    }

    public static string? ResolveApkPath(string catalogPath, QuestAppTarget app)
    {
        if (string.IsNullOrWhiteSpace(app.ApkFile))
        {
            return null;
        }

        if (Uri.TryCreate(app.ApkFile, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return uri.LocalPath;
        }

        if (Path.IsPathRooted(app.ApkFile))
        {
            return Path.GetFullPath(app.ApkFile);
        }

        var catalogDirectory = Path.GetDirectoryName(Path.GetFullPath(catalogPath)) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(catalogDirectory, app.ApkFile));
    }

    public async Task SaveExampleAsync(string path, CancellationToken cancellationToken = default)
    {
        var catalog = new QuestSessionCatalog(
            QuestSessionCatalog.CurrentSchemaVersion,
            new[]
            {
                new QuestAppTarget(
                    "rusty-xr-quest-minimal",
                    "Rusty XR Minimal Quest APK",
                    "com.example.rustyxr.minimal",
                    ".MainActivity",
                    "../../../Rusty-XR/examples/quest-minimal-apk/build/outputs/rusty-xr-quest-minimal-debug.apk",
                    "Public Rust-native Android smoke-test APK. Build it locally; APK bytes are ignored and not committed."),
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
                    "Generic development profile for moderate thermal load."),
                new DeviceProfile(
                    "perf-smoke-test",
                    "Performance smoke test",
                    new[]
                    {
                        new DeviceProperty("debug.oculus.cpuLevel", "3"),
                        new DeviceProperty("debug.oculus.gpuLevel", "3")
                    },
                    "Short verification profile for launch and frame-callback checks.")
            },
            new[]
            {
                new RuntimeProfile(
                    "minimal-contract-log",
                    "Minimal contract log",
                    new Dictionary<string, string>
                    {
                        ["rustyxr.example"] = "quest-minimal-apk",
                        ["rustyxr.contracts"] = "synthetic"
                    },
                    "Documents that the minimal APK displays synthetic Rusty XR contract JSON and frame callback status."),
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
