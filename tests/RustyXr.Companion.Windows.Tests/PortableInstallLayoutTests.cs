using RustyXr.Companion.Windows;

namespace RustyXr.Companion.Windows.Tests;

public sealed class PortableInstallLayoutTests
{
    [Fact]
    public void ReleaseInstallRootIsGuardedAgainstDevAndSiblingPaths()
    {
        var localAppData = Path.Combine(Path.GetTempPath(), $"rusty-xr-appdata-{Guid.NewGuid():N}");
        var releaseRoot = Path.Combine(localAppData, "Programs", "RustyXrCompanion");
        var devRoot = Path.Combine(localAppData, "Programs", "RustyXrCompanionDev");
        var siblingRoot = Path.Combine(localAppData, "Programs", "RustyXrCompanionOld");

        Assert.Equal(releaseRoot, PortableInstallLayout.ReleaseInstallRoot(localAppData));
        Assert.True(PortableInstallLayout.IsExpectedReleaseInstallRoot(releaseRoot, localAppData));
        Assert.True(PortableInstallLayout.IsExpectedReleaseInstallRoot(releaseRoot + Path.DirectorySeparatorChar, localAppData));
        Assert.False(PortableInstallLayout.IsExpectedReleaseInstallRoot(devRoot, localAppData));
        Assert.False(PortableInstallLayout.IsExpectedReleaseInstallRoot(siblingRoot, localAppData));
    }

    [Fact]
    public void DataRootIsGuardedAgainstNestedOrSiblingPaths()
    {
        var localAppData = Path.Combine(Path.GetTempPath(), $"rusty-xr-appdata-{Guid.NewGuid():N}");
        var dataRoot = Path.Combine(localAppData, "RustyXrCompanion");

        Assert.Equal(dataRoot, PortableInstallLayout.DataRoot(localAppData));
        Assert.True(PortableInstallLayout.IsExpectedDataRoot(dataRoot, localAppData));
        Assert.False(PortableInstallLayout.IsExpectedDataRoot(Path.Combine(dataRoot, "diagnostics"), localAppData));
        Assert.False(PortableInstallLayout.IsExpectedDataRoot(Path.Combine(localAppData, "RustyXrCompanionDev"), localAppData));
    }
}
