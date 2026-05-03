namespace RustyXr.Companion.Core;

public static class CompanionContentLayout
{
    public const string CatalogDirectoryName = "catalogs";
    public const string DefaultCatalogFileName = "rusty-xr-quest-composite-layer.catalog.json";
    public const string CompositeQuestApkFileName = "rusty-xr-quest-composite-layer-debug.apk";
    public const string BrokerQuestApkFileName = "rusty-xr-quest-broker-debug.apk";
    public const string DefaultCatalogAppId = "rusty-xr-quest-composite-layer";
    public const string DefaultDeviceProfileId = "xr-composite-smoke-test";
    public const string DefaultRuntimeProfileId = "camera-stereo-gpu-composite";

    public static string DefaultCatalogPath(string? baseDirectory = null)
        => Path.Combine(
            baseDirectory ?? AppContext.BaseDirectory,
            CatalogDirectoryName,
            DefaultCatalogFileName);

    public static string BundledCompositeApkPath(string? baseDirectory = null)
        => Path.Combine(
            baseDirectory ?? AppContext.BaseDirectory,
            CatalogDirectoryName,
            "apks",
            CompositeQuestApkFileName);

    public static string BundledBrokerApkPath(string? baseDirectory = null)
        => Path.Combine(
            baseDirectory ?? AppContext.BaseDirectory,
            CatalogDirectoryName,
            "apks",
            BrokerQuestApkFileName);

    public static string FallbackSampleCatalogPath()
        => Path.Combine("samples", "quest-session-kit", "apk-catalog.example.json");

    public static bool BundledCompositeCatalogIsComplete(string? baseDirectory = null)
        => File.Exists(DefaultCatalogPath(baseDirectory)) &&
           File.Exists(BundledCompositeApkPath(baseDirectory));

    public static string DefaultOrFallbackCatalogPath(string? baseDirectory = null)
    {
        return BundledCompositeCatalogIsComplete(baseDirectory)
            ? DefaultCatalogPath(baseDirectory)
            : FallbackSampleCatalogPath();
    }
}
