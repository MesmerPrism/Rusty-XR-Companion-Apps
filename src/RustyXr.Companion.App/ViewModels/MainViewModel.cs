using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using RustyXr.Companion.Core;
using RustyXr.Companion.Diagnostics;
using RustyXr.Companion.Windows;

namespace RustyXr.Companion.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ToolLocator _toolLocator = new();
    private readonly QuestAdbService _adbService;
    private readonly ScrcpyService _scrcpyService;
    private readonly HzdbService _hzdbService;
    private readonly WindowsEnvironmentAnalyzer _analyzer;
    private readonly CatalogLoader _catalogLoader = new();
    private readonly PortableReleaseUpdateService _updateService = new();
    private readonly AppBuildIdentity _buildIdentity = AppBuildIdentity.Detect();

    private string _status = "Ready.";
    private string _buildLabel;
    private string _updateStatus;
    private string _selectedSerial = string.Empty;
    private string _endpoint = "192.168.1.2:5555";
    private string _catalogPath = CompanionContentLayout.DefaultOrFallbackCatalogPath();
    private string _apkPath = string.Empty;
    private string _packageName = "com.example.questapp";
    private string _activityName = string.Empty;
    private string _deviceProfileId = "balanced-dev";
    private string _runtimeProfileId = string.Empty;
    private string _settleMilliseconds = "2500";
    private string _cpuLevel = "2";
    private string _gpuLevel = "2";
    private string _lastSnapshot = "No headset snapshot captured yet.";
    private string _lastVisualProof = "No visual proof captured yet.";
    private string _proximityDurationMs = "28800000";
    private string _screenshotMethod = "screencap";
    private string _mediaPort = "8787";
    private string _mediaOutputRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RustyXrCompanion",
        "media-stream");
    private QuestSessionCatalog? _catalog;
    private QuestAppTarget? _selectedCatalogApp;

    public MainViewModel()
    {
        _buildLabel = _buildIdentity.DisplayLabel;
        _updateStatus = _buildIdentity.AutoUpdatesEnabled
            ? "Release updates enabled."
            : "Release updates disabled for this channel.";
        _adbService = new QuestAdbService(_toolLocator);
        _scrcpyService = new ScrcpyService(_toolLocator);
        _hzdbService = new HzdbService(_toolLocator, _adbService);
        _analyzer = new WindowsEnvironmentAnalyzer(_toolLocator, _adbService);

        RefreshToolsCommand = new AsyncRelayCommand(RefreshToolsAsync);
        InstallToolingCommand = new AsyncRelayCommand(InstallToolingAsync);
        RefreshDevicesCommand = new AsyncRelayCommand(RefreshDevicesAsync);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync);
        EnableWifiAdbCommand = new AsyncRelayCommand(EnableWifiAdbAsync, HasSerial);
        SnapshotCommand = new AsyncRelayCommand(SnapshotAsync, HasSerial);
        BrowseCatalogCommand = new AsyncRelayCommand(BrowseCatalogAsync);
        LoadCatalogCommand = new AsyncRelayCommand(LoadCatalogAsync);
        UseCatalogAppCommand = new AsyncRelayCommand(UseCatalogAppAsync, HasCatalogApp);
        VerifyCatalogAppCommand = new AsyncRelayCommand(VerifyCatalogAppAsync, HasSerialAndCatalogApp);
        BrowseApkCommand = new AsyncRelayCommand(BrowseApkAsync);
        InstallCommand = new AsyncRelayCommand(InstallAsync, HasSerial);
        LaunchCommand = new AsyncRelayCommand(LaunchAsync, HasSerial);
        StopCommand = new AsyncRelayCommand(StopAsync, HasSerial);
        ApplyProfileCommand = new AsyncRelayCommand(ApplyProfileAsync, HasSerial);
        StartCastCommand = new AsyncRelayCommand(StartCastAsync, HasSerial);
        ReverseMediaPortCommand = new AsyncRelayCommand(ReverseMediaPortAsync, HasSerial);
        ReceiveMediaOnceCommand = new AsyncRelayCommand(ReceiveMediaOnceAsync);
        CaptureScreenshotCommand = new AsyncRelayCommand(CaptureScreenshotAsync, HasSerial);
        KeepAwakeCommand = new AsyncRelayCommand(KeepAwakeAsync, HasSerial);
        RestoreProximityCommand = new AsyncRelayCommand(RestoreProximityAsync, HasSerial);
        ReadProximityCommand = new AsyncRelayCommand(ReadProximityAsync, HasSerial);
        WakeHeadsetCommand = new AsyncRelayCommand(WakeHeadsetAsync, HasSerial);
        DiagnosticsCommand = new AsyncRelayCommand(WriteDiagnosticsAsync);
    }

    public ObservableCollection<ToolStatus> Tools { get; } = new();
    public ObservableCollection<QuestDevice> Devices { get; } = new();
    public ObservableCollection<QuestAppTarget> CatalogApps { get; } = new();
    public ObservableCollection<string> Log { get; } = new();

    public string BuildLabel
    {
        get => _buildLabel;
        set => SetProperty(ref _buildLabel, value);
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        set => SetProperty(ref _updateStatus, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string SelectedSerial
    {
        get => _selectedSerial;
        set
        {
            if (SetProperty(ref _selectedSerial, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string Endpoint
    {
        get => _endpoint;
        set => SetProperty(ref _endpoint, value);
    }

    public string CatalogPath
    {
        get => _catalogPath;
        set => SetProperty(ref _catalogPath, value);
    }

    public QuestAppTarget? SelectedCatalogApp
    {
        get => _selectedCatalogApp;
        set
        {
            if (SetProperty(ref _selectedCatalogApp, value))
            {
                UseCatalogAppCommand.RaiseCanExecuteChanged();
                VerifyCatalogAppCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ApkPath
    {
        get => _apkPath;
        set => SetProperty(ref _apkPath, value);
    }

    public string PackageName
    {
        get => _packageName;
        set => SetProperty(ref _packageName, value);
    }

    public string ActivityName
    {
        get => _activityName;
        set => SetProperty(ref _activityName, value);
    }

    public string DeviceProfileId
    {
        get => _deviceProfileId;
        set => SetProperty(ref _deviceProfileId, value);
    }

    public string RuntimeProfileId
    {
        get => _runtimeProfileId;
        set => SetProperty(ref _runtimeProfileId, value);
    }

    public string SettleMilliseconds
    {
        get => _settleMilliseconds;
        set => SetProperty(ref _settleMilliseconds, value);
    }

    public string CpuLevel
    {
        get => _cpuLevel;
        set => SetProperty(ref _cpuLevel, value);
    }

    public string GpuLevel
    {
        get => _gpuLevel;
        set => SetProperty(ref _gpuLevel, value);
    }

    public string LastSnapshot
    {
        get => _lastSnapshot;
        set => SetProperty(ref _lastSnapshot, value);
    }

    public string LastVisualProof
    {
        get => _lastVisualProof;
        set => SetProperty(ref _lastVisualProof, value);
    }

    public string ProximityDurationMs
    {
        get => _proximityDurationMs;
        set => SetProperty(ref _proximityDurationMs, value);
    }

    public string ScreenshotMethod
    {
        get => _screenshotMethod;
        set => SetProperty(ref _screenshotMethod, value);
    }

    public AsyncRelayCommand RefreshToolsCommand { get; }
    public AsyncRelayCommand InstallToolingCommand { get; }
    public AsyncRelayCommand RefreshDevicesCommand { get; }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand EnableWifiAdbCommand { get; }
    public AsyncRelayCommand SnapshotCommand { get; }
    public AsyncRelayCommand BrowseCatalogCommand { get; }
    public AsyncRelayCommand LoadCatalogCommand { get; }
    public AsyncRelayCommand UseCatalogAppCommand { get; }
    public AsyncRelayCommand VerifyCatalogAppCommand { get; }
    public AsyncRelayCommand BrowseApkCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public AsyncRelayCommand LaunchCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand ApplyProfileCommand { get; }
    public AsyncRelayCommand StartCastCommand { get; }
    public AsyncRelayCommand ReverseMediaPortCommand { get; }
    public AsyncRelayCommand ReceiveMediaOnceCommand { get; }
    public AsyncRelayCommand CaptureScreenshotCommand { get; }
    public AsyncRelayCommand KeepAwakeCommand { get; }
    public AsyncRelayCommand RestoreProximityCommand { get; }
    public AsyncRelayCommand ReadProximityCommand { get; }
    public AsyncRelayCommand WakeHeadsetCommand { get; }
    public AsyncRelayCommand DiagnosticsCommand { get; }

    public string MediaPort
    {
        get => _mediaPort;
        set => SetProperty(ref _mediaPort, value);
    }

    public string MediaOutputRoot
    {
        get => _mediaOutputRoot;
        set => SetProperty(ref _mediaOutputRoot, value);
    }

    public async Task InitializeAsync()
    {
        RepairReleaseInstallRegistration();
        await CheckForReleaseUpdateAsync().ConfigureAwait(true);
        await LoadDefaultCatalogIfAvailableAsync().ConfigureAwait(true);
        await RefreshToolsAsync().ConfigureAwait(true);
        await RefreshDevicesAsync().ConfigureAwait(true);
    }

    private void RepairReleaseInstallRegistration()
    {
        if (_buildIdentity.Channel != AppInstallChannel.Release ||
            string.IsNullOrWhiteSpace(_buildIdentity.InstallRoot))
        {
            return;
        }

        try
        {
            PortableInstallRegistration.CreateReleaseShortcut(_buildIdentity.InstallRoot);
            PortableInstallRegistration.RegisterReleaseInstall(_buildIdentity.InstallRoot, _buildIdentity.CurrentVersion);
            AddLog("Release uninstall registration checked.");
        }
        catch (Exception exception)
        {
            AddLog($"Release uninstall registration skipped: {exception.Message}");
        }
    }

    private async Task CheckForReleaseUpdateAsync()
    {
        if (!_buildIdentity.AutoUpdatesEnabled)
        {
            AddLog($"{_buildIdentity.ChannelLabel}: public release auto-update is disabled.");
            return;
        }

        try
        {
            UpdateStatus = "Checking for release updates...";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var update = await _updateService.CheckAsync(_buildIdentity, timeout.Token).ConfigureAwait(true);
            UpdateStatus = update.Detail;
            AddLog(update.Detail);

            if (!update.UpdateAvailable)
            {
                return;
            }

            Status = "Updating release install...";
            await _updateService
                .StartUpdateAsync(_buildIdentity, update, Environment.ProcessId, timeout.Token)
                .ConfigureAwait(true);
            AddLog("Release updater launched; this app instance will close and restart from the updated install.");
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            UpdateStatus = $"Release update check skipped: {exception.Message}";
            AddLog(UpdateStatus);
        }
    }

    private async Task RefreshToolsAsync()
    {
        await RunUiActionAsync("Refreshing tooling...", async () =>
        {
            Tools.Clear();
            foreach (var tool in await _toolLocator.GetToolStatusesAsync().ConfigureAwait(true))
            {
                Tools.Add(tool);
            }

            AddLog($"Tooling scan found {Tools.Count(static tool => tool.IsAvailable)} available tool(s).");
        }).ConfigureAwait(true);
    }

    private async Task InstallToolingAsync()
    {
        await RunUiActionAsync("Installing managed Quest tooling...", async () =>
        {
            using var service = new OfficialQuestToolingService();
            var progress = new Progress<OfficialQuestToolingProgress>(item =>
            {
                Status = $"{item.Status} ({item.PercentComplete}%)";
                AddLog($"{item.Status}: {item.Detail}");
            });
            var result = await service.InstallOrUpdateAsync(progress).ConfigureAwait(true);
            AddLog($"{result.Summary} {result.Detail}");
            await RefreshToolsAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task RefreshDevicesAsync()
    {
        await RunUiActionAsync("Refreshing Quest devices...", async () =>
        {
            Devices.Clear();
            foreach (var device in await _adbService.ListDevicesAsync().ConfigureAwait(true))
            {
                Devices.Add(device);
            }

            if (string.IsNullOrWhiteSpace(SelectedSerial))
            {
                SelectedSerial = Devices.FirstOrDefault(static device => device.IsOnline)?.Serial ?? string.Empty;
            }

            AddLog(Devices.Count == 0 ? "No ADB devices reported." : $"ADB reported {Devices.Count} device(s).");
        }).ConfigureAwait(true);
    }

    private async Task ConnectAsync()
    {
        await RunUiActionAsync("Connecting over Wi-Fi ADB...", async () =>
        {
            if (!QuestEndpoint.TryParse(Endpoint, out var endpoint))
            {
                throw new InvalidOperationException("Endpoint must look like host or host:port.");
            }

            var result = await _adbService.ConnectAsync(endpoint).ConfigureAwait(true);
            AddLog(result.CondensedOutput);
            await RefreshDevicesAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task EnableWifiAdbAsync()
    {
        await RunUiActionAsync("Enabling Wi-Fi ADB...", async () =>
        {
            var result = await _adbService.EnableWifiAdbAsync(SelectedSerial).ConfigureAwait(true);
            Endpoint = result.Endpoint.ToString();
            SelectedSerial = result.Endpoint.ToString();
            AddLog(result.Succeeded
                ? $"Wi-Fi ADB enabled at {result.Endpoint}."
                : $"Wi-Fi ADB attempted at {result.Endpoint}; verify device list.");
            if (!string.IsNullOrWhiteSpace(result.Detail))
            {
                AddLog(result.Detail);
            }

            await RefreshDevicesAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task SnapshotAsync()
    {
        await RunUiActionAsync("Capturing headset snapshot...", async () =>
        {
            var snapshot = await _adbService.GetSnapshotAsync(SelectedSerial).ConfigureAwait(true);
            LastSnapshot =
                $"Serial: {snapshot.Serial}{Environment.NewLine}" +
                $"Model: {snapshot.Model}{Environment.NewLine}" +
                $"Battery: {snapshot.Battery}{Environment.NewLine}" +
                $"Wakefulness: {snapshot.Wakefulness}{Environment.NewLine}" +
                $"Foreground: {snapshot.Foreground}{Environment.NewLine}" +
                $"Captured: {snapshot.CapturedAt:t}";
            AddLog("Snapshot captured.");
        }).ConfigureAwait(true);
    }

    private Task BrowseCatalogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Quest app catalogs (*.json)|*.json|All files (*.*)|*.*",
            Title = "Select a Quest app catalog"
        };

        if (dialog.ShowDialog() == true)
        {
            CatalogPath = dialog.FileName;
            AddLog($"Selected catalog: {CatalogPath}");
        }

        return Task.CompletedTask;
    }

    private async Task LoadCatalogAsync()
    {
        await RunUiActionAsync("Loading catalog...", async () =>
        {
            await LoadCatalogCoreAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task LoadDefaultCatalogIfAvailableAsync()
    {
        if (!CompanionContentLayout.BundledCompositeCatalogIsComplete())
        {
            AddLog("Bundled release catalog/APK pair not found; use Browse or Load for a local catalog.");
            return;
        }

        try
        {
            CatalogPath = CompanionContentLayout.DefaultCatalogPath();
            DeviceProfileId = CompanionContentLayout.DefaultDeviceProfileId;
            RuntimeProfileId = CompanionContentLayout.DefaultRuntimeProfileId;
            await LoadCatalogCoreAsync(CompanionContentLayout.DefaultCatalogAppId).ConfigureAwait(true);
            AddLog("Bundled Rusty XR composite-layer catalog loaded.");
        }
        catch (Exception exception)
        {
            AddLog($"Bundled release catalog could not be loaded: {exception.Message}");
        }
    }

    private async Task LoadCatalogCoreAsync(string? preferredAppId = null)
    {
        _catalog = await _catalogLoader.LoadAsync(CatalogPath).ConfigureAwait(true);
        CatalogApps.Clear();
        foreach (var app in _catalog.Apps)
        {
            CatalogApps.Add(app);
        }

        SelectedCatalogApp = string.IsNullOrWhiteSpace(preferredAppId)
            ? CatalogApps.FirstOrDefault()
            : CatalogApps.FirstOrDefault(app => string.Equals(app.Id, preferredAppId, StringComparison.OrdinalIgnoreCase))
              ?? CatalogApps.FirstOrDefault();
        AddLog($"Loaded {CatalogApps.Count} catalog app(s).");
        if (SelectedCatalogApp is not null)
        {
            await UseCatalogAppAsync().ConfigureAwait(true);
        }
    }

    private Task UseCatalogAppAsync()
    {
        if (SelectedCatalogApp is null)
        {
            return Task.CompletedTask;
        }

        PackageName = SelectedCatalogApp.PackageName;
        ActivityName = SelectedCatalogApp.ActivityName ?? string.Empty;
        ApkPath = CatalogLoader.ResolveApkPath(CatalogPath, SelectedCatalogApp) ?? ApkPath;
        AddLog($"Using catalog app: {SelectedCatalogApp.Label}");
        return Task.CompletedTask;
    }

    private Task BrowseApkAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Android packages (*.apk)|*.apk|All files (*.*)|*.*",
            Title = "Select a Quest APK"
        };

        if (dialog.ShowDialog() == true)
        {
            ApkPath = dialog.FileName;
            AddLog($"Selected APK: {ApkPath}");
        }

        return Task.CompletedTask;
    }

    private async Task InstallAsync()
    {
        await RunUiActionAsync("Installing APK...", async () =>
        {
            var result = await _adbService.InstallAsync(SelectedSerial, ApkPath).ConfigureAwait(true);
            AddLog(result.CondensedOutput);
        }).ConfigureAwait(true);
    }

    private async Task LaunchAsync()
    {
        await RunUiActionAsync("Launching target app...", async () =>
        {
            var result = await _adbService.LaunchAsync(
                SelectedSerial,
                PackageName,
                string.IsNullOrWhiteSpace(ActivityName) ? null : ActivityName).ConfigureAwait(true);
            AddLog(result.CondensedOutput);
        }).ConfigureAwait(true);
    }

    private async Task StopAsync()
    {
        await RunUiActionAsync("Stopping target app...", async () =>
        {
            var result = await _adbService.StopAsync(SelectedSerial, PackageName).ConfigureAwait(true);
            AddLog(result.CondensedOutput);
        }).ConfigureAwait(true);
    }

    private async Task ApplyProfileAsync()
    {
        await RunUiActionAsync("Applying device profile...", async () =>
        {
            int? cpu = int.TryParse(CpuLevel, out var parsedCpu) ? parsedCpu : null;
            int? gpu = int.TryParse(GpuLevel, out var parsedGpu) ? parsedGpu : null;
            var results = await _adbService
                .ApplyDeviceProfileAsync(SelectedSerial, cpu, gpu, new Dictionary<string, string>())
                .ConfigureAwait(true);
            foreach (var result in results)
            {
                AddLog(result.CondensedOutput);
            }
        }).ConfigureAwait(true);
    }

    private async Task VerifyCatalogAppAsync()
    {
        await RunUiActionAsync("Verifying catalog app...", async () =>
        {
            if (_catalog is null)
            {
                _catalog = await _catalogLoader.LoadAsync(CatalogPath).ConfigureAwait(true);
            }

            if (SelectedCatalogApp is null)
            {
                throw new InvalidOperationException("Load a catalog and select an app first.");
            }

            await UseCatalogAppAsync().ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(DeviceProfileId))
            {
                var profile = _catalog.DeviceProfiles.FirstOrDefault(profile =>
                    string.Equals(profile.Id, DeviceProfileId, StringComparison.OrdinalIgnoreCase));
                if (profile is null)
                {
                    throw new InvalidOperationException($"Device profile '{DeviceProfileId}' was not found.");
                }

                var properties = profile.Properties.ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal);
                foreach (var result in await _adbService.ApplyDeviceProfileAsync(SelectedSerial, null, null, properties).ConfigureAwait(true))
                {
                    AddLog(result.CondensedOutput);
                }
            }

            var runtimeProfile = ResolveRuntimeProfile(_catalog, RuntimeProfileId);
            if (runtimeProfile is not null)
            {
                AddLog($"Using runtime profile: {runtimeProfile.Label}");
            }

            if (!string.IsNullOrWhiteSpace(ApkPath) && File.Exists(ApkPath))
            {
                var install = await _adbService.InstallAsync(SelectedSerial, ApkPath).ConfigureAwait(true);
                AddLog(install.CondensedOutput);
            }
            else
            {
                AddLog("Catalog APK path is missing; skipping install and verifying the installed package.");
            }

            var launch = await _adbService.LaunchAsync(
                SelectedSerial,
                PackageName,
                string.IsNullOrWhiteSpace(ActivityName) ? null : ActivityName,
                runtimeProfile?.Values).ConfigureAwait(true);
            AddLog(launch.CondensedOutput);

            var settleMs = int.TryParse(SettleMilliseconds, out var parsedSettle) ? parsedSettle : 2500;
            if (settleMs > 0)
            {
                await Task.Delay(settleMs).ConfigureAwait(true);
            }

            var snapshot = await _adbService.GetSnapshotAsync(SelectedSerial).ConfigureAwait(true);
            LastSnapshot =
                $"Serial: {snapshot.Serial}{Environment.NewLine}" +
                $"Model: {snapshot.Model}{Environment.NewLine}" +
                $"Battery: {snapshot.Battery}{Environment.NewLine}" +
                $"Wakefulness: {snapshot.Wakefulness}{Environment.NewLine}" +
                $"Foreground: {snapshot.Foreground}{Environment.NewLine}" +
                $"Captured: {snapshot.CapturedAt:t}";

            var diagnostics = await _adbService.GetAppDiagnosticsAsync(SelectedSerial, PackageName).ConfigureAwait(true);
            AddLog($"Verify: running={diagnostics.ProcessRunning}; pid={diagnostics.ProcessId ?? "none"}; foreground={diagnostics.Foreground}; gfx={diagnostics.GfxInfoSummary}; memory={diagnostics.MemorySummary}");
        }).ConfigureAwait(true);
    }

    private Task StartCastAsync()
    {
        return RunUiActionAsync("Starting display cast...", () =>
        {
            var session = _scrcpyService.Start(new StreamLaunchRequest(SelectedSerial, MaxSize: 1280, BitRateMbps: 12));
            AddLog($"Started scrcpy process {session.ProcessId}.");
            return Task.CompletedTask;
        });
    }

    private async Task ReverseMediaPortAsync()
    {
        await RunUiActionAsync("Configuring MediaProjection adb reverse...", async () =>
        {
            var port = int.TryParse(MediaPort, out var parsedPort) ? parsedPort : MediaFrameReceiverService.DefaultPort;
            var result = await _adbService.ReverseTcpAsync(SelectedSerial, port, port).ConfigureAwait(true);
            AddLog(result.CondensedOutput.Length > 0
                ? result.CondensedOutput
                : $"ADB reverse configured for tcp:{port}.");
        }).ConfigureAwait(true);
    }

    private async Task ReceiveMediaOnceAsync()
    {
        await RunUiActionAsync("Waiting for one media frame...", async () =>
        {
            var port = int.TryParse(MediaPort, out var parsedPort) ? parsedPort : MediaFrameReceiverService.DefaultPort;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var result = await new MediaFrameReceiverService()
                .ReceiveAsync("127.0.0.1", port, MediaOutputRoot, once: true, timeout.Token)
                .ConfigureAwait(true);
            AddLog($"Media receiver captured {result.FrameCount} frame(s) under {result.OutputDirectory}.");
            LastVisualProof =
                $"Media receiver: {result.FrameCount} frame(s){Environment.NewLine}" +
                $"Port: {result.Port}{Environment.NewLine}" +
                $"Output: {result.OutputDirectory}{Environment.NewLine}" +
                $"Latest: {result.Frames.LastOrDefault()?.PayloadPath ?? "none"}";
        }).ConfigureAwait(true);
    }

    private async Task CaptureScreenshotAsync()
    {
        await RunUiActionAsync("Capturing headset screenshot...", async () =>
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RustyXrCompanion",
                "screenshots");
            var outputPath = HzdbService.CreateDefaultScreenshotPath(root, SelectedSerial);
            var capture = await _hzdbService
                .CaptureScreenshotAsync(SelectedSerial, outputPath, ScreenshotMethod)
                .ConfigureAwait(true);
            LastVisualProof =
                $"Screenshot: {(capture.Succeeded ? "captured" : "failed")}{Environment.NewLine}" +
                $"Method: {capture.Method}{Environment.NewLine}" +
                $"Path: {capture.OutputPath}{Environment.NewLine}" +
                $"Detail: {capture.Detail}";
            AddLog(capture.Succeeded
                ? $"Screenshot captured: {capture.OutputPath}"
                : $"Screenshot failed: {capture.Detail}");
        }).ConfigureAwait(true);
    }

    private async Task KeepAwakeAsync()
    {
        await RunUiActionAsync("Disabling wear sensor for keep-awake...", async () =>
        {
            var durationMs = int.TryParse(ProximityDurationMs, out var parsedDuration) ? parsedDuration : 28_800_000;
            var result = await _hzdbService
                .SetProximityAsync(SelectedSerial, enableNormalProximity: false, durationMs)
                .ConfigureAwait(true);
            AddLog(result.CondensedOutput.Length > 0
                ? result.CondensedOutput
                : $"Keep-awake proximity hold requested for {durationMs} ms.");
            await ReadProximityCoreAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task RestoreProximityAsync()
    {
        await RunUiActionAsync("Restoring normal proximity behavior...", async () =>
        {
            var result = await _hzdbService
                .SetProximityAsync(SelectedSerial, enableNormalProximity: true)
                .ConfigureAwait(true);
            AddLog(result.CondensedOutput.Length > 0
                ? result.CondensedOutput
                : "Normal proximity behavior requested.");
            await ReadProximityCoreAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private async Task ReadProximityAsync()
    {
        await RunUiActionAsync("Reading proximity status...", ReadProximityCoreAsync).ConfigureAwait(true);
    }

    private async Task WakeHeadsetAsync()
    {
        await RunUiActionAsync("Sending hzdb wake request...", async () =>
        {
            var result = await _hzdbService.WakeDeviceAsync(SelectedSerial).ConfigureAwait(true);
            AddLog(result.CondensedOutput.Length > 0 ? result.CondensedOutput : "Wake request sent.");
        }).ConfigureAwait(true);
    }

    private async Task ReadProximityCoreAsync()
    {
        var status = await _hzdbService.GetProximityStatusAsync(SelectedSerial).ConfigureAwait(true);
        LastVisualProof =
            $"Proximity: {status.Detail}{Environment.NewLine}" +
            $"Virtual state: {status.VirtualState}{Environment.NewLine}" +
            $"Autosleep disabled: {status.IsAutosleepDisabled}{Environment.NewLine}" +
            $"Headset state: {status.HeadsetState}{Environment.NewLine}" +
            $"Hold until: {status.HoldUntil?.ToString("G") ?? "unknown"}";
        AddLog(status.Detail);
    }

    private async Task WriteDiagnosticsAsync()
    {
        await RunUiActionAsync("Writing diagnostics bundle...", async () =>
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RustyXrCompanion",
                "diagnostics");
            var report = await _analyzer.AnalyzeAsync(includeSnapshots: true).ConfigureAwait(true);
            var folder = await new DiagnosticsBundleWriter().WriteAsync(report, root).ConfigureAwait(true);
            AddLog($"Diagnostics written to {folder}");
        }).ConfigureAwait(true);
    }

    private async Task RunUiActionAsync(string runningStatus, Func<Task> action)
    {
        try
        {
            Status = runningStatus;
            await action().ConfigureAwait(true);
            Status = "Ready.";
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            AddLog($"Error: {exception.Message}");
        }
    }

    private bool HasSerial() => !string.IsNullOrWhiteSpace(SelectedSerial);
    private bool HasCatalogApp() => SelectedCatalogApp is not null;
    private bool HasSerialAndCatalogApp() => HasSerial() && HasCatalogApp();

    private static RuntimeProfile? ResolveRuntimeProfile(QuestSessionCatalog catalog, string? runtimeProfileId)
    {
        if (string.IsNullOrWhiteSpace(runtimeProfileId))
        {
            return null;
        }

        var profile = catalog.RuntimeProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, runtimeProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            throw new InvalidOperationException($"Runtime profile '{runtimeProfileId}' was not found.");
        }

        return profile;
    }

    private void RaiseCommandStates()
    {
        EnableWifiAdbCommand.RaiseCanExecuteChanged();
        SnapshotCommand.RaiseCanExecuteChanged();
        VerifyCatalogAppCommand.RaiseCanExecuteChanged();
        InstallCommand.RaiseCanExecuteChanged();
        LaunchCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        ApplyProfileCommand.RaiseCanExecuteChanged();
        StartCastCommand.RaiseCanExecuteChanged();
        ReverseMediaPortCommand.RaiseCanExecuteChanged();
        CaptureScreenshotCommand.RaiseCanExecuteChanged();
        KeepAwakeCommand.RaiseCanExecuteChanged();
        RestoreProximityCommand.RaiseCanExecuteChanged();
        ReadProximityCommand.RaiseCanExecuteChanged();
        WakeHeadsetCommand.RaiseCanExecuteChanged();
    }

    private void AddLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Log.Insert(0, $"[{DateTime.Now:T}] {message}");
        while (Log.Count > 200)
        {
            Log.RemoveAt(Log.Count - 1);
        }
    }
}
