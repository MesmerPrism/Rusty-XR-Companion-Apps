using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
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
    public async Task CatalogLoaderPreservesHttpApkAssetUrl()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rusty-xr-catalog-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """
            {
              "schemaVersion": "rusty.xr.quest-app-catalog.v1",
              "apps": [
                {
                  "id": "example",
                  "label": "Example",
                  "packageName": "com.example.questapp",
                  "activityName": ".MainActivity",
                  "apkFile": "https://example.invalid/releases/example.apk",
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

            Assert.Equal("https://example.invalid/releases/example.apk", selection.ResolvedApkPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CatalogLoaderExampleIncludesCurrentHandMeshAndDepthProfiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rusty-xr-catalog-{Guid.NewGuid():N}.json");

        try
        {
            var loader = new CatalogLoader();
            await loader.SaveExampleAsync(path);
            var catalog = await loader.LoadAsync(path);

            Assert.Contains(catalog.RuntimeProfiles, profile => profile.Id == "meta-hand-mesh-particles");
            Assert.Contains(catalog.RuntimeProfiles, profile => profile.Id == "environment-depth-mesh-overlay");
            Assert.Contains(catalog.RuntimeProfiles, profile => profile.Id == "environment-depth-particle-overlay");
            Assert.Single(catalog.RuntimeProfiles, profile =>
                profile.Id.Contains("hand-mesh", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CompanionContentLayoutPrefersBundledCatalogWhenPresent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rusty-xr-content-{Guid.NewGuid():N}");
        try
        {
            var catalogPath = CompanionContentLayout.DefaultCatalogPath(root);
            var apkPath = CompanionContentLayout.BundledCompositeApkPath(root);
            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(apkPath)!);
            File.WriteAllText(catalogPath, "{}");
            File.WriteAllText(apkPath, "apk");

            Assert.True(CompanionContentLayout.BundledCompositeCatalogIsComplete(root));
            Assert.Equal(catalogPath, CompanionContentLayout.DefaultOrFallbackCatalogPath(root));
            Assert.EndsWith(
                Path.Combine("catalogs", "apks", CompanionContentLayout.CompositeQuestApkFileName),
                apkPath,
                StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(
                Path.Combine("catalogs", "apks", CompanionContentLayout.BrokerQuestApkFileName),
                CompanionContentLayout.BundledBrokerApkPath(root),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CompanionContentLayoutFallsBackToSampleCatalog()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rusty-xr-content-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CompanionContentLayout.DefaultCatalogPath(root))!);
            File.WriteAllText(CompanionContentLayout.DefaultCatalogPath(root), "{}");

            Assert.False(CompanionContentLayout.BundledCompositeCatalogIsComplete(root));
            Assert.Equal(
                CompanionContentLayout.FallbackSampleCatalogPath(),
                CompanionContentLayout.DefaultOrFallbackCatalogPath(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
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
        Assert.Equal("Rusty XR Companion", release.AppDisplayName);
        Assert.Equal("MesmerPrism.RustyXR.Companion", release.AppUserModelId);
        Assert.Equal(AppInstallChannel.Dev, dev.Channel);
        Assert.False(dev.AutoUpdatesEnabled);
        Assert.Equal("Rusty XR Companion Dev", dev.AppDisplayName);
        Assert.Equal("MesmerPrism.RustyXR.Companion.Dev", dev.AppUserModelId);
        Assert.Equal(AppInstallChannel.Source, source.Channel);
        Assert.False(source.AutoUpdatesEnabled);
        Assert.Equal("Rusty XR Companion Source", source.AppDisplayName);
        Assert.Equal("MesmerPrism.RustyXR.Companion.Source", source.AppUserModelId);
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
    public void QuestAdbServiceParsesHeadsetPowerStatus()
    {
        var output = """
            Power Manager State:
              mWakefulness=Awake
              mInteractive=true
            Display Power: state=ON
            """;

        var status = QuestAdbService.ParseQuestPowerStatus(output);

        Assert.Equal("Awake", status.Wakefulness);
        Assert.True(status.IsInteractive);
        Assert.True(status.IsAwake);
        Assert.Equal("ON", status.DisplayPowerState);
        Assert.Contains("wakefulness Awake", status.Detail);
    }

    [Fact]
    public void QuestAdbServiceParsesControllerBatteryStatuses()
    {
        var output = """
            Tracking state:
              Left
                [id: 41, conn: CONNECTED_ACTIVE, battery: 88]
              Right -- primary
                [id: 42, conn: CONNECTED_ACTIVE, battery: 91]
            """;

        var controllers = QuestAdbService.ParseControllerStatuses(output);

        Assert.Equal(2, controllers.Count);
        Assert.Equal("Left", controllers[0].HandLabel);
        Assert.Equal(88, controllers[0].BatteryLevel);
        Assert.Equal("CONNECTED_ACTIVE", controllers[0].ConnectionState);
        Assert.Equal("Right", controllers[1].HandLabel);
        Assert.Equal(91, controllers[1].BatteryLevel);
    }

    [Fact]
    public void QuestAdbServiceParsesControllerStatusWhenHandAndEntryShareLine()
    {
        var output = """
            Right -- [id: 99, conn: CONNECTED_IDLE, battery: 57]
            """;

        var controller = Assert.Single(QuestAdbService.ParseControllerStatuses(output));

        Assert.Equal("Right", controller.HandLabel);
        Assert.Equal(57, controller.BatteryLevel);
        Assert.Equal("CONNECTED_IDLE", controller.ConnectionState);
    }

    [Fact]
    public void QuestAdbServiceDetectsForegroundGuardianBlocker()
    {
        var activityOutput = """
            ACTIVITY MANAGER ACTIVITIES
              topResumedActivity=ActivityRecord{123 u0 com.example.questapp/.MainActivity t44}
            """;
        var windowOutput = """
            WINDOW MANAGER WINDOWS
              mCurrentFocus=Window{abc u0 com.oculus.guardian/.GuardianDialogActivity}
              mFocusedApp=ActivityRecord{def u0 com.example.questapp/.MainActivity t44}
            """;

        var status = QuestAdbService.ParseQuestForegroundStatus(activityOutput, windowOutput);

        Assert.Equal("com.example.questapp/.MainActivity", status.ResumedActivity);
        Assert.Equal("com.oculus.guardian/.GuardianDialogActivity", status.FocusedWindow);
        Assert.True(status.HasKnownBlocker);
        Assert.True(status.FocusDiffersFromResumed);
        Assert.Equal("Guardian", status.BlockerLabel);
    }

    [Fact]
    public void QuestAdbServiceDetectsRuntimePermissionPrompt()
    {
        var activityOutput = """
            ACTIVITY MANAGER ACTIVITIES
              mResumedActivity: ActivityRecord{123 u0 com.example.questapp/.MainActivity t44}
            """;
        var windowOutput = """
            WINDOW MANAGER WINDOWS
              mCurrentFocus=Window{abc u0 com.android.permissioncontroller/com.android.permissioncontroller.permission.ui.GrantPermissionsActivity}
            """;

        var status = QuestAdbService.ParseQuestForegroundStatus(activityOutput, windowOutput);

        Assert.True(status.HasKnownBlocker);
        Assert.Equal("Permission request", status.BlockerLabel);
        Assert.Contains("focused window", status.Detail);
    }

    [Fact]
    public async Task QuestAdbServiceLaunchPassesRuntimeProfileExtras()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"rusty-xr-adb-{Guid.NewGuid():N}");
        var adbPath = Path.Combine(tempRoot, "adb.exe");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(adbPath, string.Empty);
        var previousAdb = Environment.GetEnvironmentVariable("RUSTY_XR_ADB");
        Environment.SetEnvironmentVariable("RUSTY_XR_ADB", adbPath);

        try
        {
            var runner = new RecordingCommandRunner();
            var service = new QuestAdbService(new ToolLocator(runner), runner);

            var result = await service.LaunchAsync(
                "SERIAL",
                "com.example.questapp",
                ".MainActivity",
                new Dictionary<string, string>
                {
                    ["rustyxr.camera"] = "true",
                    ["rustyxr.cameraWidth"] = "1280",
                    ["rustyxr.cameraRawOverlayOverscan"] = "1.06",
                    ["rustyxr.xrRenderScale"] = "0.75",
                    ["rustyxr.mediaProjectionDelayMs"] = "5000",
                    ["rustyxr.source"] = "headset-camera"
                });

            Assert.True(result.Succeeded);
            Assert.Equal(adbPath, result.FileName);
            Assert.Contains("-s SERIAL shell am start -n 'com.example.questapp/.MainActivity'", result.Arguments);
            Assert.Contains("--ez 'rustyxr.camera' true", result.Arguments);
            Assert.Contains("--ei 'rustyxr.cameraWidth' 1280", result.Arguments);
            Assert.Contains("--ef 'rustyxr.cameraRawOverlayOverscan' 1.06", result.Arguments);
            Assert.Contains("--ef 'rustyxr.xrRenderScale' 0.75", result.Arguments);
            Assert.Contains("--ei 'rustyxr.mediaProjectionDelayMs' 5000", result.Arguments);
            Assert.Contains("--es 'rustyxr.source' 'headset-camera'", result.Arguments);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RUSTY_XR_ADB", previousAdb);
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task QuestAdbServicePullsRunAsTextFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"rusty-xr-adb-{Guid.NewGuid():N}");
        var adbPath = Path.Combine(tempRoot, "adb.exe");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(adbPath, string.Empty);
        var previousAdb = Environment.GetEnvironmentVariable("RUSTY_XR_ADB");
        Environment.SetEnvironmentVariable("RUSTY_XR_ADB", adbPath);

        try
        {
            var runner = new RecordingCommandRunner();
            var service = new QuestAdbService(new ToolLocator(runner), runner);

            var result = await service.ReadRunAsTextFileAsync(
                "SERIAL",
                "com.example.questapp",
                "files/camera-source-diagnostics.json");

            Assert.True(result.Succeeded);
            Assert.Equal(adbPath, result.FileName);
            Assert.Contains("-s SERIAL shell run-as 'com.example.questapp' cat 'files/camera-source-diagnostics.json'", result.Arguments);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RUSTY_XR_ADB", previousAdb);
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task QuestAdbServiceSendsSleepKeyevent()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"rusty-xr-adb-{Guid.NewGuid():N}");
        var adbPath = Path.Combine(tempRoot, "adb.exe");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(adbPath, string.Empty);
        var previousAdb = Environment.GetEnvironmentVariable("RUSTY_XR_ADB");
        Environment.SetEnvironmentVariable("RUSTY_XR_ADB", adbPath);

        try
        {
            var runner = new RecordingCommandRunner();
            var service = new QuestAdbService(new ToolLocator(runner), runner);

            var result = await service.SleepDeviceAsync("SERIAL");

            Assert.True(result.Succeeded);
            Assert.Equal(adbPath, result.FileName);
            Assert.Contains("-s SERIAL shell input keyevent SLEEP", result.Arguments);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RUSTY_XR_ADB", previousAdb);
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task HzdbServiceWakeAddsKeepAwakeHold()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"rusty-xr-hzdb-{Guid.NewGuid():N}");
        var hzdbPath = Path.Combine(tempRoot, "hzdb.exe");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(hzdbPath, string.Empty);
        var previousHzdb = Environment.GetEnvironmentVariable("RUSTY_XR_HZDB");
        Environment.SetEnvironmentVariable("RUSTY_XR_HZDB", hzdbPath);

        try
        {
            var runner = new RecordingCommandRunner();
            var service = new HzdbService(new ToolLocator(runner), runner: runner);

            var result = await service.WakeDeviceAsync("SERIAL");

            Assert.True(result.Succeeded);
            Assert.Collection(
                runner.Calls,
                call => Assert.Contains("device wake --device \"SERIAL\"", call.Arguments),
                call => Assert.Contains("device proximity --device \"SERIAL\" --disable --duration-ms 28800000", call.Arguments));
            Assert.Contains("device wake --device \"SERIAL\"", result.Arguments);
            Assert.Contains("device proximity --device \"SERIAL\" --disable --duration-ms 28800000", result.Arguments);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RUSTY_XR_HZDB", previousHzdb);
            Directory.Delete(tempRoot, recursive: true);
        }
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
    public void BrokerShellHelperCommandBuildsBackgroundProximityWatchdog()
    {
        var command = BrokerShellHelperService.BuildAppProcessShellCommand(
            BrokerShellHelperDefaults.DeviceJarPath,
            "127.0.0.1",
            BrokerClientService.DefaultPort,
            disconnect: false,
            proximityWatchdog: true);

        Assert.StartsWith("sh -c ", command, StringComparison.Ordinal);
        Assert.Contains("--proximity-watchdog", command, StringComparison.Ordinal);
        Assert.Contains("--proximity-watchdog-duration-ms 28800000", command, StringComparison.Ordinal);
        Assert.Contains("--proximity-watchdog-hold-duration-ms 28800000", command, StringComparison.Ordinal);
        Assert.Contains("--proximity-watchdog-interval-ms 5000", command, StringComparison.Ordinal);
        Assert.Contains(BrokerShellHelperDefaults.ProximityWatchdogLogPath, command, StringComparison.Ordinal);
        Assert.EndsWith("&'", command, StringComparison.Ordinal);
    }

    [Fact]
    public void BrokerShellHelperCommandBuildsBackgroundFocusGuardian()
    {
        var command = BrokerShellHelperService.BuildAppProcessShellCommand(
            BrokerShellHelperDefaults.DeviceJarPath,
            "127.0.0.1",
            BrokerClientService.DefaultPort,
            disconnect: false,
            focusGuardian: true,
            focusGuardianMode: "toggle_broker_target",
            focusTargetPackage: "org.example.target",
            focusTargetActivity: "org.example.target.MainActivity");

        Assert.StartsWith("sh -c ", command, StringComparison.Ordinal);
        Assert.Contains("--focus-guardian", command, StringComparison.Ordinal);
        Assert.Contains("--focus-guardian-mode", command, StringComparison.Ordinal);
        Assert.Contains("toggle_broker_target", command, StringComparison.Ordinal);
        Assert.Contains("--focus-target-package", command, StringComparison.Ordinal);
        Assert.Contains("org.example.target", command, StringComparison.Ordinal);
        Assert.Contains("--focus-target-activity", command, StringComparison.Ordinal);
        Assert.Contains("org.example.target.MainActivity", command, StringComparison.Ordinal);
        Assert.Contains(BrokerShellHelperDefaults.FocusGuardianLogPath, command, StringComparison.Ordinal);
        Assert.EndsWith("&'", command, StringComparison.Ordinal);
    }

    [Fact]
    public void BrokerShellHelperCommandCanStopWatchdogAndStartFocusGuardianInBackground()
    {
        var command = BrokerShellHelperService.BuildAppProcessShellCommand(
            BrokerShellHelperDefaults.DeviceJarPath,
            "127.0.0.1",
            BrokerClientService.DefaultPort,
            disconnect: false,
            stopProximityWatchdog: true,
            focusGuardian: true);

        Assert.StartsWith("sh -c ", command, StringComparison.Ordinal);
        Assert.Contains("--stop-proximity-watchdog", command, StringComparison.Ordinal);
        Assert.Contains("--focus-guardian", command, StringComparison.Ordinal);
        Assert.Contains(BrokerShellHelperDefaults.FocusGuardianLogPath, command, StringComparison.Ordinal);
        Assert.EndsWith("&'", command, StringComparison.Ordinal);
    }

    [Fact]
    public void BrokerShellHelperStopRequestsProximityWatchdogStop()
    {
        var options = new BrokerShellHelperRunOptions(
                "SERIAL",
                RustyXrRoot: Path.GetTempPath(),
                BuildBeforeRun: false,
                Disconnect: true)
            .Normalize();
        var command = BrokerShellHelperService.BuildAppProcessShellCommand(
            options.DeviceJarPath,
            options.BrokerHost,
            options.BrokerPort,
            options.Disconnect,
            stopProximityWatchdog: options.StopProximityWatchdog);

        Assert.Contains("--disconnect", command, StringComparison.Ordinal);
        Assert.Contains("--stop-proximity-watchdog", command, StringComparison.Ordinal);
        Assert.DoesNotContain("sh -c", command, StringComparison.Ordinal);
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

    [Fact]
    public void ManagedMediaToolingServiceParsesPreferredFfmpegReleaseAsset()
    {
        var json = """
            {
              "html_url": "https://github.com/BtbN/FFmpeg-Builds/releases/tag/latest",
              "assets": [
                {
                  "name": "ffmpeg-master-latest-win64-lgpl-shared.zip",
                  "browser_download_url": "https://example.invalid/master.zip",
                  "digest": "sha256:1111111111111111111111111111111111111111111111111111111111111111"
                },
                {
                  "name": "ffmpeg-n7.1-latest-win64-lgpl-shared-7.1.zip",
                  "browser_download_url": "https://example.invalid/7.1.zip",
                  "digest": "sha256:2222222222222222222222222222222222222222222222222222222222222222"
                },
                {
                  "name": "ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip",
                  "browser_download_url": "https://example.invalid/8.1.zip",
                  "digest": "sha256:3333333333333333333333333333333333333333333333333333333333333333"
                },
                {
                  "name": "checksums.sha256",
                  "browser_download_url": "https://example.invalid/checksums.sha256"
                }
              ]
            }
            """;

        var release = ManagedMediaToolingService.ParseFfmpegReleaseMetadataJson(json);

        Assert.Equal("ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip", release.AssetName);
        Assert.Equal("https://example.invalid/8.1.zip", release.DownloadUri);
        Assert.Equal("3333333333333333333333333333333333333333333333333333333333333333", release.ChecksumSha256);
        Assert.Equal("8.1-latest+sha.333333333333", release.Version);
        Assert.Equal("https://example.invalid/checksums.sha256", release.Sha256SumsUri);
    }

    [Fact]
    public void ManagedMediaToolingServiceParsesFfmpegChecksumsFile()
    {
        var checksum = ManagedMediaToolingService.ParseSha256SumsFile(
            """
            aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa *ffmpeg-master-latest-win64-gpl-shared.zip
            bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb *ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip
            """,
            "ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip");

        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", checksum);
    }

    [Theory]
    [InlineData("configuration: --enable-shared --disable-libx264 --disable-libx265", "LGPL-compatible", true, false, false)]
    [InlineData("configuration: --enable-shared --enable-gpl --disable-nonfree", "GPL", false, true, false)]
    [InlineData("configuration: --enable-shared --enable-nonfree", "nonfree", false, false, true)]
    [InlineData("ffmpeg version n8.1", "unknown", false, false, false)]
    public void FfmpegRuntimeClassifierClassifiesConfigureFlags(
        string versionOutput,
        string licenseClass,
        bool approved,
        bool enableGpl,
        bool enableNonfree)
    {
        var classification = FfmpegRuntimeClassifier.ClassifyVersionOutput(versionOutput);

        Assert.Equal(licenseClass, classification.LicenseClass);
        Assert.Equal(approved, classification.ApprovedForDefaultUse);
        Assert.Equal(enableGpl, classification.EnableGpl);
        Assert.Equal(enableNonfree, classification.EnableNonfree);
    }

    [Fact]
    public void SourceWorkspaceGuideDetectsSiblingReposAndApkOutputs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rusty-xr-workspace-{Guid.NewGuid():N}");
        var rustyXr = Path.Combine(root, SourceWorkspaceGuide.RustyXrRepoName);
        var companion = Path.Combine(root, SourceWorkspaceGuide.CompanionRepoName);
        try
        {
            Directory.CreateDirectory(rustyXr);
            Directory.CreateDirectory(companion);
            File.WriteAllText(Path.Combine(rustyXr, "Cargo.toml"), string.Empty);
            File.WriteAllText(Path.Combine(companion, "RustyXr.Companion.slnx"), string.Empty);
            Directory.CreateDirectory(Path.Combine(rustyXr, "examples", "quest-minimal-apk", "build", "outputs"));
            File.WriteAllText(
                Path.Combine(rustyXr, "examples", "quest-minimal-apk", "build", "outputs", "rusty-xr-quest-minimal-debug.apk"),
                "apk");

            var status = SourceWorkspaceGuide.Evaluate(root);

            Assert.True(status.RustyXrRepoPresent);
            Assert.True(status.CompanionRepoPresent);
            Assert.True(status.MinimalApkPresent);
            Assert.False(status.CompositeApkPresent);
            Assert.False(status.BrokerApkPresent);
            Assert.Contains(status.Commands, command => command.Id == "verify-minimal-apk");
            Assert.Contains(status.Commands, command => command.Id == "verify-broker-apk");
            Assert.Contains("Rusty XR Source Workspace", SourceWorkspaceGuide.ToMarkdown(status));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CompanionOperationSurfaceMapsApiCliAndMcpNames()
    {
        var catalog = CompanionOperationSurface.Create();

        Assert.Equal(CompanionOperationSurface.SchemaVersion, catalog.SchemaVersion);
        Assert.Contains(catalog.Operations, operation => operation.Id == "workspace.guide");
        Assert.Contains(catalog.Operations, operation => operation.Id == "android.agent_command");
        Assert.Contains(catalog.Operations, operation => operation.Id == "broker.h264_proxy_probe");
        Assert.Contains(catalog.Operations, operation => operation.Id == "broker.h264_proxy_start");
        Assert.All(catalog.Operations, operation =>
        {
            Assert.StartsWith("rusty_xr_", operation.McpToolName, StringComparison.Ordinal);
            Assert.DoesNotContain(" ", operation.McpToolName, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(operation.CliTemplate));
            Assert.False(string.IsNullOrWhiteSpace(operation.ApiSurface));
        });
    }

    [Fact]
    public void CompanionOperationSurfaceFiltersAndroidOwner()
    {
        var androidCatalog = CompanionOperationSurface.Filter(CompanionOperationSurface.Create(), "android");

        var operation = Assert.Single(androidCatalog.Operations);
        Assert.Equal("android.agent_command", operation.Id);
        Assert.Equal("phone-agent-gated", operation.Safety);
    }

    [Fact]
    public void CompanionOperationSurfaceExportsMcpToolList()
    {
        var windowsCatalog = CompanionOperationSurface.Filter(CompanionOperationSurface.Create(), "windows");
        var tools = CompanionOperationSurface.ToMcpToolList(windowsCatalog);

        Assert.Equal(CompanionOperationSurface.McpToolsSchemaVersion, tools.SchemaVersion);
        Assert.DoesNotContain(tools.Tools, tool => tool.Binding.Owner == "android-companion");
        Assert.Contains(tools.Tools, tool => tool.Name == "rusty_xr_verify_catalog" && tool.Annotations.DestructiveHint);
        Assert.Contains(
            tools.Tools,
            tool => tool.Name == "rusty_xr_workspace_guide" &&
                    tool.Annotations.ReadOnlyHint &&
                    tool.InputSchema["properties"]?["root"]?["type"]?.GetValue<string>() == "string");
        Assert.Contains(
            tools.Tools,
            tool => tool.Name == "rusty_xr_broker_h264_proxy_probe" &&
                    tool.Annotations.DestructiveHint &&
                    tool.InputSchema["properties"]?["packetCount"]?["type"]?.GetValue<string>() == "integer");
    }

    [Fact]
    public void CompanionOperationPlannerPlansReadOnlyBrokerStatus()
    {
        var plan = CompanionOperationPlanner.CreatePlan(
            "broker.status",
            new Dictionary<string, string>
            {
                ["host"] = "127.0.0.1",
                ["port"] = "8765"
            });

        Assert.Equal(CompanionOperationPlanner.SchemaVersion, plan.SchemaVersion);
        Assert.True(plan.Allowed);
        Assert.False(plan.RequiresSideEffectOptIn);
        Assert.Equal("rusty-xr-companion", plan.Executable);
        Assert.Equal(
            new[] { "broker", "status", "--host", "127.0.0.1", "--port", "8765", "--json" },
            plan.Arguments);
    }

    [Fact]
    public void CompanionOperationPlannerGatesStateChangingCatalogVerify()
    {
        var inputs = new Dictionary<string, string>
        {
            ["path"] = "catalog.json",
            ["app"] = "broker",
            ["serial"] = "ABC123",
            ["install"] = "true"
        };

        var blocked = CompanionOperationPlanner.CreatePlan("catalog.verify", inputs);
        var allowed = CompanionOperationPlanner.CreatePlan("catalog.verify", inputs, allowSideEffects: true);

        Assert.False(blocked.Allowed);
        Assert.True(blocked.RequiresSideEffectOptIn);
        Assert.Contains("--install", blocked.Arguments);
        Assert.True(allowed.Allowed);
        Assert.Contains("runner-level confirmation", allowed.GateReason);
    }

    [Fact]
    public void CompanionOperationPlannerBuildsH264ProxyCommands()
    {
        var probe = CompanionOperationPlanner.CreatePlan(
            "broker.h264_proxy_probe",
            new Dictionary<string, string>
            {
                ["serial"] = "ABC123",
                ["packetCount"] = "4",
                ["packetBytes"] = "96",
                ["timeoutMs"] = "10000"
            });

        Assert.False(probe.Allowed);
        Assert.True(probe.RequiresSideEffectOptIn);
        Assert.Equal(
            new[]
            {
                "broker",
                "h264-proxy-probe",
                "--serial",
                "ABC123",
                "--packet-count",
                "4",
                "--packet-bytes",
                "96",
                "--timeout-ms",
                "10000",
                "--json"
            },
            probe.Arguments);

        var start = CompanionOperationPlanner.CreatePlan(
            "broker.h264_proxy_start",
            new Dictionary<string, string>
            {
                ["remoteHost"] = "192.168.1.25",
                ["remotePort"] = "8879",
                ["localBindHost"] = "0.0.0.0",
                ["localLanEnabled"] = "true"
            },
            allowSideEffects: true);

        Assert.True(start.Allowed);
        Assert.Contains("--local-lan-enabled", start.Arguments);
        Assert.Equal(
            new[]
            {
                "broker",
                "h264-proxy-start",
                "--remote-host",
                "192.168.1.25",
                "--remote-port",
                "8879",
                "--local-bind-host",
                "0.0.0.0",
                "--local-lan-enabled",
                "--json"
            },
            start.Arguments);
    }

    [Fact]
    public void CompanionOperationPlannerBuildsAndroidAgentCommand()
    {
        var plan = CompanionOperationPlanner.CreatePlan(
            "android.agent_command",
            new Dictionary<string, string>
            {
                ["phoneSerial"] = "PHONE123",
                ["command"] = "quest-launch",
                ["endpoint"] = "192.168.1.25:5555",
                ["packageId"] = "com.example.target",
                ["runtimeExtras"] = "{\"rustyxr.profile\":\"smoke\"}",
                ["allowDevSession"] = "true"
            },
            allowSideEffects: true);

        Assert.True(plan.Allowed);
        Assert.Equal("adb", plan.Executable);
        Assert.Equal(
            new[]
            {
                "-s",
                "PHONE123",
                "shell",
                "am",
                "start",
                "-a",
                "io.github.mesmerprism.rustyxr.companion.android.RUN_AGENT_COMMAND",
                "-n",
                "io.github.mesmerprism.rustyxr.companion.android/.agent.AgentCommandActivity",
                "--es",
                "command",
                "quest-launch",
                "--es",
                "endpoint",
                "192.168.1.25:5555",
                "--es",
                "package_id",
                "com.example.target",
                "--es",
                "extras_json",
                "{\"rustyxr.profile\":\"smoke\"}",
                "--ez",
                "allow_dev_session",
                "true"
            },
            plan.Arguments);
    }

    [Fact]
    public void CompanionOperationPlannerRejectsUnknownInputs()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CompanionOperationPlanner.CreatePlan(
                "broker.status",
                new Dictionary<string, string> { ["privatePackage"] = "example" }));

        Assert.Contains("does not accept input", exception.Message);
    }

    [Fact]
    public void RuntimeProfileLogValidatorRequiresRealGpuProjectionForFinalProfile()
    {
        var probeProfile = new RuntimeProfile(
            "camera-gpu-buffer-probe",
            "GPU buffer probe",
            new Dictionary<string, string> { ["rustyxr.cameraTier"] = "gpu-buffer-probe" },
            "Probe only.");
        var finalProfile = new RuntimeProfile(
            "camera-stereo-gpu-composite",
            "Stereo GPU composite",
            new Dictionary<string, string> { ["rustyxr.cameraTier"] = "gpu-projected" },
            "Real stereo renderer.");

        Assert.False(RuntimeProfileLogValidator.RequiresAlignedGpuProjection(probeProfile));
        Assert.True(RuntimeProfileLogValidator.RequiresAlignedGpuProjection(finalProfile));
        var passingLog = string.Join(
            Environment.NewLine,
            "Rusty XR OpenXR state FOCUSED",
            "Rusty XR final projection status frame=1 openXrFrameCount=360 openXrFocused=true activeTier=gpu-projected alignedProjection=true stereoLayout=Separate pairedLeftRightGpuBuffers=true poseSource=estimated-profile sourceEyeMapping=display-left-from-left-source displayLeftCameraId=50 displayRightCameraId=51 leftCameraTextureTransform=rotate0 rightCameraTextureTransform=rotate0 cameraTextureTransformSource=public-check cameraTextureTransformReason=upright orientationCheck=true orientationAccepted=true cpuUploadCount=0 projectionShaderPath=projected noHardwareBufferLifetimeWarnings=true visualInspection=accepted visualReleaseAccepted=true",
            "Rusty XR GPU stereo camera draw prepared frame=1 activeTier=gpu-projected alignedProjection=true stereoLayout=Separate pairedLeftRightGpuBuffers=true poseSource=estimated-profile leftCameraTextureTransform=rotate0 rightCameraTextureTransform=rotate0 orientationCheck=true orientationAccepted=true cpuUploadCount=0 projectionShaderPath=projected",
            "Rusty XR OpenXR frame 360 rendered 1260x1320");
        var releaseBlockedLog = string.Join(
            Environment.NewLine,
            "Rusty XR OpenXR state FOCUSED",
            "Rusty XR final projection status frame=1 openXrFrameCount=360 openXrFocused=true activeTier=gpu-projected alignedProjection=false stereoLayout=Separate pairedLeftRightGpuBuffers=true poseSource=estimated-profile sourceEyeMapping=display-left-from-left-source displayLeftCameraId=50 displayRightCameraId=51 leftCameraTextureTransform=rotate0 rightCameraTextureTransform=rotate0 cameraTextureTransformSource=public-check cameraTextureTransformReason=upright orientationCheck=true orientationAccepted=false cpuUploadCount=0 projectionShaderPath=projected noHardwareBufferLifetimeWarnings=true visualInspection=required visualReleaseAccepted=false",
            "Rusty XR OpenXR frame 360 rendered 1260x1320");
        var scatteredLog = string.Join(
            Environment.NewLine,
            "Rusty XR final projection status frame=1 openXrFrameCount=360 openXrFocused=true activeTier=gpu-buffer-probe alignedProjection=false",
            "activeTier=gpu-projected",
            "alignedProjection=true",
            "stereoLayout=Separate",
            "poseSource=platform",
            "pairedLeftRightGpuBuffers=true",
            "cpuUploadCount=0");

        Assert.True(RuntimeProfileLogValidator.Validate(finalProfile, passingLog).Succeeded);
        var releaseBlockedResult = RuntimeProfileLogValidator.Validate(finalProfile, releaseBlockedLog);
        Assert.False(releaseBlockedResult.Succeeded);
        Assert.Contains("observed activeTier=gpu-projected alignedProjection=false", releaseBlockedResult.Detail);
        Assert.Contains("manual visual release acceptance", releaseBlockedResult.Detail);
        Assert.False(RuntimeProfileLogValidator.Validate(
            finalProfile,
            "Rusty XR final projection status frame=1 openXrFrameCount=360 openXrFocused=true activeTier=gpu-projected alignedProjection=true stereoLayout=Separate pairedLeftRightGpuBuffers=true poseSource=estimated-profile cpuUploadCount=0 projectionShaderPath=projected noHardwareBufferLifetimeWarnings=true").Succeeded);
        Assert.False(RuntimeProfileLogValidator.Validate(finalProfile, "activeTier=gpu-buffer-probe alignedProjection=false").Succeeded);
        Assert.False(RuntimeProfileLogValidator.Validate(finalProfile, scatteredLog).Succeeded);
        Assert.False(RuntimeProfileLogValidator.Validate(finalProfile, null).Succeeded);
    }

    [Fact]
    public void RuntimeProfileLogValidatorRequiresEnvironmentDepthStatusForDepthProfile()
    {
        var profile = new RuntimeProfile(
            "environment-depth-diagnostics",
            "Environment depth diagnostics",
            new Dictionary<string, string>
            {
                ["rustyxr.depth"] = "visualize",
                ["rustyxr.camera"] = "false"
            },
            "Depth diagnostics.");
        var passingLog = string.Join(
            Environment.NewLine,
            "Rusty XR OpenXR state FOCUSED",
            "Rusty XR environment depth status frame=240 depthEnabled=true mode=visualize extensionAvailable=true supported=true providerCreated=true providerRunning=true swapchainCreated=true size=320x320 depthFormat=VK_FORMAT_D16_UNORM layerCount=2 swapchainIndex=1 openXrFrameCount=240 observedOpenXrFps=72.0 acquireAttempts=240 acquiredFrames=120 unavailableFrames=120 acquireErrors=0 uniqueCaptureTimes=30 repeatedCaptureTimes=90 observedAcquireHz=72.0 observedDepthHz=9.0 lastAcquireCpuMs=0.060 avgAcquireCpuMs=0.055 captureTimeNs=123456 nearZ=0.1 farZ=4 handRemovalSupported=true handRemovalEnabled=false confidenceSource=none confidencePayload=false confidenceStatus=not-exposed-by-XR_META_environment_depth visualizer=true depthVisualEncoding=linear-d16-meters-infinity-white depthVisualMaxMeters=20 depthVisualTextureTransform=rotate0+flipY depthVisualEyeMapping=left-layer-0-right-layer-1",
            "Rusty XR environment depth visualizer draw frame=240 swapchainIndex=1 captureTimeNs=123456 renderTarget=1372x1512 depthTextureFormat=VK_FORMAT_D16_UNORM depthTextureLayers=2 grayscale=linear-d16-meters-infinity-white depthVisualMaxMeters=20 depthVisualTextureTransform=rotate0+flipY confidenceSource=none confidencePayload=false confidenceStatus=not-exposed-by-XR_META_environment_depth");
        var noFrameLog =
            "Rusty XR environment depth status frame=240 depthEnabled=true mode=visualize extensionAvailable=true supported=true providerCreated=true providerRunning=true swapchainCreated=true size=320x320 depthFormat=VK_FORMAT_D16_UNORM layerCount=2 swapchainIndex=none openXrFrameCount=240 observedOpenXrFps=72.0 acquireAttempts=240 acquiredFrames=0 unavailableFrames=240 acquireErrors=0 uniqueCaptureTimes=0 repeatedCaptureTimes=0 observedAcquireHz=72.0 observedDepthHz=0.0 lastAcquireCpuMs=0.040 avgAcquireCpuMs=0.040 captureTimeNs=none nearZ=0 farZ=0 handRemovalSupported=true handRemovalEnabled=false confidenceSource=none confidencePayload=false confidenceStatus=not-exposed-by-XR_META_environment_depth visualizer=true depthVisualEncoding=linear-d16-meters-infinity-white depthVisualMaxMeters=20 depthVisualTextureTransform=rotate0+flipY depthVisualEyeMapping=left-layer-0-right-layer-1";

        Assert.True(RuntimeProfileLogValidator.RequiresEnvironmentDepthDiagnostics(profile));
        Assert.True(RuntimeProfileLogValidator.RequiresLogValidation(profile));
        Assert.True(RuntimeProfileLogValidator.Validate(profile, passingLog).Succeeded);
        var result = RuntimeProfileLogValidator.Validate(profile, noFrameLog);
        Assert.False(result.Succeeded);
        Assert.Contains("acquiredFrames>0", result.Detail);
        Assert.Contains("runtime captureTimeNs", result.Detail);
        Assert.False(RuntimeProfileLogValidator.Validate(profile, null).Succeeded);
    }

    [Fact]
    public void RuntimeProfileLogValidatorRequiresOverlayDrawForDepthOverlayProfiles()
    {
        var meshProfile = new RuntimeProfile(
            "environment-depth-mesh-overlay",
            "Environment depth mesh overlay",
            new Dictionary<string, string>
            {
                ["rustyxr.depth"] = "mesh-overlay"
            },
            "Depth mesh overlay.");
        var particleProfile = new RuntimeProfile(
            "environment-depth-particle-overlay",
            "Environment depth particle overlay",
            new Dictionary<string, string>
            {
                ["rustyxr.depth"] = "particle-overlay"
            },
            "Depth particle overlay.");
        var baseLog = string.Join(
            Environment.NewLine,
            "Rusty XR environment depth status frame=240 depthEnabled=true mode=mesh-overlay extensionAvailable=true supported=true providerCreated=true providerRunning=true swapchainCreated=true size=320x320 depthFormat=VK_FORMAT_D16_UNORM layerCount=2 swapchainIndex=1 openXrFrameCount=240 observedOpenXrFps=72.0 acquireAttempts=240 acquiredFrames=120 unavailableFrames=120 acquireErrors=0 uniqueCaptureTimes=30 repeatedCaptureTimes=90 observedAcquireHz=72.0 observedDepthHz=9.0 lastAcquireCpuMs=0.060 avgAcquireCpuMs=0.055 captureTimeNs=123456 nearZ=0.1 farZ=4 handRemovalSupported=true handRemovalEnabled=false confidenceSource=none confidencePayload=false confidenceStatus=not-exposed-by-XR_META_environment_depth visualizer=true depthVisualEncoding=linear-d16-meters-infinity-white depthVisualMaxMeters=20 depthVisualTextureTransform=rotate0+flipY depthVisualEyeMapping=left-layer-0-right-layer-1",
            "Rusty XR environment depth visualizer draw frame=240 swapchainIndex=1 captureTimeNs=123456 renderTarget=1372x1512 depthTextureFormat=VK_FORMAT_D16_UNORM depthTextureLayers=2 grayscale=linear-d16-meters-infinity-white depthVisualMaxMeters=20 depthVisualTextureTransform=rotate0+flipY confidenceSource=none confidencePayload=false confidenceStatus=not-exposed-by-XR_META_environment_depth");
        var meshDraw =
            "Rusty XR environment depth mesh overlay draw frame=240 swapchainIndex=1 cellMeters=0.05 discontinuityMeters=0.2 distanceColorMaxMeters=3 distanceColorSource=environment-depth-meters captureTimeNs=123456 renderTarget=1372x1512 depthTexture=320x320 depthTextureFormat=VK_FORMAT_D16_UNORM depthTextureLayers=2 depthVisualTextureTransform=rotate0+flipY projection=local-space-depth-surface rasterization=world-space-generated-grid gridStridePixels=8 generatedVertexCount=3840 historyFramesDrawn=1 historyMaxAgeMs=0 dominantSurfaceGrid=true screenUvGrid=false passthroughVisible=true";
        var particleDraw =
            "Rusty XR environment depth particle overlay draw frame=240 swapchainIndex=1 distanceColorMaxMeters=3 distanceColorSource=environment-depth-meters captureTimeNs=123456 renderTarget=1372x1512 depthTexture=320x320 depthTextureFormat=VK_FORMAT_D16_UNORM depthTextureLayers=2 projection=local-space-retained-particles rasterization=metric-billboard-particles particleCapacity=8192 particleVertexCount=49152 sampleStridePixels=8 confidenceSource=depth-discontinuity confidenceThresholdMeters=0.2 passthroughVisible=true";

        Assert.True(RuntimeProfileLogValidator.RequiresEnvironmentDepthDiagnostics(meshProfile));
        Assert.True(RuntimeProfileLogValidator.Validate(meshProfile, $"{baseLog}{Environment.NewLine}{meshDraw}").Succeeded);
        var missingMesh = RuntimeProfileLogValidator.Validate(meshProfile, baseLog);
        Assert.False(missingMesh.Succeeded);
        Assert.Contains("local-space depth mesh overlay draw", missingMesh.Detail);
        Assert.True(RuntimeProfileLogValidator.Validate(particleProfile, $"{baseLog}{Environment.NewLine}{particleDraw}").Succeeded);
    }

    [Fact]
    public void RuntimeProfileLogValidatorRequiresMetaHandMeshParticleLogs()
    {
        var profile = new RuntimeProfile(
            "meta-hand-mesh-particles",
            "Meta hand mesh particles",
            new Dictionary<string, string>
            {
                ["rustyxr.handParticles"] = "meta"
            },
            "Meta hand mesh particles.");
        var passingLog = string.Join(
            Environment.NewLine,
            "Rusty XR OpenXR hand mesh particles frame=240 activeHands=2 particles=128 leftStatus=Updated rightStatus=Updated firstTierNeighborLinks=512 secondTierNeighborLinks=512 crossHandNeighborLinks=64 crossHandMaxDistanceMeters=0.2 crossHandNeighborsPerPoint=2 skinning=cpu-linear-blend bindMesh=XR_FB_hand_tracking_mesh jointSource=xrLocateHandJointsEXT",
            "Rusty XR hand mesh particle draw frame=240 mode=openxr-reference-space-skinned-fb-hand-mesh particles=128 vertexCount=768 projection=stereo-billboards sampler=LiveHandMeshParticleSampler passthroughVisible=true");

        Assert.True(RuntimeProfileLogValidator.RequiresHandMeshParticles(profile));
        Assert.True(RuntimeProfileLogValidator.RequiresLogValidation(profile));
        Assert.True(RuntimeProfileLogValidator.Validate(profile, passingLog).Succeeded);
        Assert.False(RuntimeProfileLogValidator.RequiresHandMeshParticles(new RuntimeProfile(
            "legacy-procedural-hand-particles",
            "Legacy procedural hand particles",
            new Dictionary<string, string>
            {
                ["rustyxr.handParticles"] = "fixture"
            },
            "Removed iteration profile.")));
        var missingRuntimeMesh = RuntimeProfileLogValidator.Validate(
            profile,
            "Rusty XR hand mesh particle draw frame=240 mode=openxr-reference-space-skinned-fb-hand-mesh particles=128 vertexCount=768 projection=stereo-billboards sampler=LiveHandMeshParticleSampler passthroughVisible=true");
        Assert.False(missingRuntimeMesh.Succeeded);
        Assert.Contains("OpenXR XR_FB_hand_tracking_mesh activeHands>0", missingRuntimeMesh.Detail);
    }

    [Fact]
    public void RuntimeProfileSafetyDetectsIntentionalStrobeProfiles()
    {
        var strobeProfile = new RuntimeProfile(
            "full-field-red-black-flicker-40hz",
            "Full-field red black",
            new Dictionary<string, string>
            {
                ["rustyxr.fullFieldFlickerHz"] = "40.0",
                ["rustyxr.xrDisplayRefreshHz"] = "120.0"
            },
            "WARNING: intentional full-field strobing profile.");
        var neutralProfile = new RuntimeProfile(
            "passthrough-underlay-hotload-neutral",
            "Neutral passthrough",
            new Dictionary<string, string>
            {
                ["rustyxr.passthroughLutFlickerHz"] = "0.0"
            },
            "Neutral profile.");

        Assert.True(RuntimeProfileSafety.UsesIntentionalStrobe(strobeProfile));
        Assert.False(RuntimeProfileSafety.UsesIntentionalStrobe(neutralProfile));
    }

    [Fact]
    public void CameraSourceDiagnosticsLogExtractorReadsJsonPayload()
    {
        var logcat = "04-29 I/RustyXrHeadsetCamera: Rusty XR camera source diagnostics JSON: {\"schemaVersion\":\"rusty.xr.camera-source-diagnostics.v1\",\"selectedStereoPairScore\":42,\"selectedStereoPairReason\":\"selected concurrent-separate 50/51\",\"sources\":[{\"cameraId\":\"50\",\"intrinsicCalibration\":[1,2,3,4,0],\"lensPoseTranslation\":[0.01,0.0,0.02],\"lensPoseRotation\":[0,0,0,1],\"lensPoseReference\":1}],\"stereoCandidates\":[{\"providerKind\":\"concurrent-separate\",\"leftCameraId\":\"50\",\"rightCameraId\":\"51\",\"accepted\":true,\"score\":42,\"reason\":\"accepted\"}]}";

        Assert.True(CameraSourceDiagnosticsLogExtractor.TryExtract(logcat, out var json));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            "rusty.xr.camera-source-diagnostics.v1",
            document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(42, document.RootElement.GetProperty("selectedStereoPairScore").GetInt32());
        var source = document.RootElement.GetProperty("sources")[0];
        Assert.Equal(5, source.GetProperty("intrinsicCalibration").GetArrayLength());
        Assert.Equal(3, source.GetProperty("lensPoseTranslation").GetArrayLength());
        Assert.Equal(4, source.GetProperty("lensPoseRotation").GetArrayLength());
    }

    [Fact]
    public void CameraSourceDiagnosticsLogExtractorRejectsTruncatedPayload()
    {
        var logcat = "04-29 I/RustyXrHeadsetCamera: Rusty XR camera source diagnostics JSON: {\"schemaVersion\":\"rusty.xr.camera-source-diagnostics.v1\",\"sources\":[";

        Assert.False(CameraSourceDiagnosticsLogExtractor.TryExtract(logcat, out _));
    }

    [Fact]
    public async Task MediaFrameReceiverWritesPayloadAndLedger()
    {
        var port = GetFreeTcpPort();
        var root = Path.Combine(Path.GetTempPath(), $"rusty-xr-media-{Guid.NewGuid():N}");
        var receiver = new MediaFrameReceiverService();
        var receiveTask = receiver.ReceiveAsync("127.0.0.1", port, root, once: true);
        await Task.Delay(100);

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();
            var payload = new byte[] { 1, 2, 3, 4 };
            var header = JsonSerializer.Serialize(new
            {
                byte_len = payload.Length,
                frame_index = 7,
                timestamp_ns = 123L,
                width = 1,
                height = 1,
                format = "rgba8888",
                stream = "display_composite"
            });
            var headerBytes = Encoding.UTF8.GetBytes(header);
            var prefix = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)headerBytes.Length);
            await stream.WriteAsync(prefix);
            await stream.WriteAsync(headerBytes);
            await stream.WriteAsync(payload);
        }

        var result = await receiveTask;

        try
        {
            Assert.Equal(1, result.FrameCount);
            Assert.Equal("display_composite", result.Frames[0].Stream);
            Assert.True(File.Exists(result.Frames[0].PayloadPath));
            Assert.True(File.Exists(Path.Combine(root, "frames.jsonl")));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(result.Frames[0].PayloadPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class RecordingCommandRunner : ICommandRunner
    {
        public List<(string FileName, string Arguments)> Calls { get; } = new();

        public Task<CommandResult> RunAsync(
            string fileName,
            string arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((fileName, arguments));
            return Task.FromResult(new CommandResult(fileName, arguments, 0, string.Empty, string.Empty, TimeSpan.Zero));
        }
    }
}
