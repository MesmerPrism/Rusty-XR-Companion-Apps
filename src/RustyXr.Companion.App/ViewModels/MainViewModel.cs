using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
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
    private readonly DispatcherTimer _snapshotTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    private const string StatusOkColor = "#3DDC84";
    private const string StatusWarningColor = "#FBBF24";
    private const string StatusErrorColor = "#EF4444";

    private static readonly TimeSpan SnapshotFreshDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AutoSnapshotInterval = TimeSpan.FromSeconds(30);

    private string _status = "Ready.";
    private string _buildLabel;
    private string _updateStatus;
    private string _headerDeviceStatus = "No Quest selected";
    private string _headerHeadsetBatteryStatus = "Battery --";
    private string _headerPowerStatus = "Power --";
    private string _headerControllerStatus = "Controllers --";
    private string _headerProximityStatus = "Proximity --";
    private string _headerForegroundStatus = "Foreground --";
    private string _headerDeviceStatusColor = StatusErrorColor;
    private string _headerHeadsetBatteryStatusColor = StatusWarningColor;
    private string _headerPowerStatusColor = StatusWarningColor;
    private string _headerControllerStatusColor = StatusWarningColor;
    private string _headerProximityStatusColor = StatusWarningColor;
    private string _headerForegroundStatusColor = StatusWarningColor;
    private string _snapshotFreshnessText = "No snapshot";
    private string _snapshotFreshnessDetail = "No headset snapshot captured yet.";
    private string _snapshotFreshnessColor = StatusWarningColor;
    private bool _snapshotAutoRefreshEnabled = true;
    private bool _snapshotRefreshInFlight;
    private bool _lastSnapshotRefreshFailed;
    private string _lastSnapshotRefreshFailure = string.Empty;
    private DateTimeOffset? _lastSnapshotAt;
    private DateTimeOffset? _lastSnapshotAttemptAt;
    private QuestSnapshot? _currentSnapshot;
    private QuestProximityStatus? _currentProximityStatus;
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
    private string _lastProximityStatus = "No proximity status read yet.";
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
    private RuntimeProfile? _selectedRuntimeProfile;

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
        SnapshotFreshnessCommand = new AsyncRelayCommand(SnapshotAsync, HasSerial);
        BrowseCatalogCommand = new AsyncRelayCommand(BrowseCatalogAsync);
        LoadCatalogCommand = new AsyncRelayCommand(LoadCatalogAsync);
        UseCatalogAppCommand = new AsyncRelayCommand(UseCatalogAppAsync, HasCatalogApp);
        InstallCatalogAppCommand = new AsyncRelayCommand(InstallCatalogAppAsync, HasSerialAndCatalogApp);
        LaunchCatalogAppCommand = new AsyncRelayCommand(LaunchCatalogAppAsync, HasSerialAndCatalogApp);
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
        ToggleProximityCommand = new AsyncRelayCommand(ToggleProximityAsync, HasSerial);
        TogglePowerCommand = new AsyncRelayCommand(TogglePowerAsync, HasSerial);
        DiagnosticsCommand = new AsyncRelayCommand(WriteDiagnosticsAsync);

        _snapshotTimer.Tick += async (_, _) => await OnSnapshotTimerTickAsync().ConfigureAwait(true);
        _snapshotTimer.Start();
    }

    public ObservableCollection<ToolStatus> Tools { get; } = new();
    public ObservableCollection<QuestDevice> Devices { get; } = new();
    public ObservableCollection<QuestControllerStatus> ControllerStatuses { get; } = new();
    public ObservableCollection<QuestAppTarget> CatalogApps { get; } = new();
    public ObservableCollection<RuntimeProfile> RuntimeProfiles { get; } = new();
    public ObservableCollection<string> Log { get; } = new();

    public string AppDisplayName => _buildIdentity.AppDisplayName;

    public string WindowTitle => AppDisplayName;

    public string HeaderIconSource =>
        _buildIdentity.Channel == AppInstallChannel.Dev
            ? "pack://application:,,,/Assets/RustyXrCompanionDev.png"
            : "pack://application:,,,/Assets/RustyXrCompanion.png";

    public string WindowIconSource =>
        _buildIdentity.Channel == AppInstallChannel.Dev
            ? "pack://application:,,,/Assets/RustyXrCompanionDev.ico"
            : "pack://application:,,,/Assets/RustyXrCompanion.ico";

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

    public string HeaderDeviceStatus
    {
        get => _headerDeviceStatus;
        set => SetProperty(ref _headerDeviceStatus, value);
    }

    public string HeaderHeadsetBatteryStatus
    {
        get => _headerHeadsetBatteryStatus;
        set => SetProperty(ref _headerHeadsetBatteryStatus, value);
    }

    public string HeaderPowerStatus
    {
        get => _headerPowerStatus;
        set => SetProperty(ref _headerPowerStatus, value);
    }

    public string HeaderControllerStatus
    {
        get => _headerControllerStatus;
        set => SetProperty(ref _headerControllerStatus, value);
    }

    public string HeaderProximityStatus
    {
        get => _headerProximityStatus;
        set => SetProperty(ref _headerProximityStatus, value);
    }

    public string HeaderForegroundStatus
    {
        get => _headerForegroundStatus;
        set => SetProperty(ref _headerForegroundStatus, value);
    }

    public string HeaderDeviceStatusColor
    {
        get => _headerDeviceStatusColor;
        private set => SetProperty(ref _headerDeviceStatusColor, value);
    }

    public string HeaderHeadsetBatteryStatusColor
    {
        get => _headerHeadsetBatteryStatusColor;
        private set => SetProperty(ref _headerHeadsetBatteryStatusColor, value);
    }

    public string HeaderPowerStatusColor
    {
        get => _headerPowerStatusColor;
        private set => SetProperty(ref _headerPowerStatusColor, value);
    }

    public string HeaderControllerStatusColor
    {
        get => _headerControllerStatusColor;
        private set => SetProperty(ref _headerControllerStatusColor, value);
    }

    public string HeaderProximityStatusColor
    {
        get => _headerProximityStatusColor;
        private set => SetProperty(ref _headerProximityStatusColor, value);
    }

    public string HeaderForegroundStatusColor
    {
        get => _headerForegroundStatusColor;
        private set => SetProperty(ref _headerForegroundStatusColor, value);
    }

    public string SnapshotFreshnessText
    {
        get => _snapshotFreshnessText;
        private set => SetProperty(ref _snapshotFreshnessText, value);
    }

    public string SnapshotFreshnessDetail
    {
        get => _snapshotFreshnessDetail;
        private set => SetProperty(ref _snapshotFreshnessDetail, value);
    }

    public string SnapshotFreshnessColor
    {
        get => _snapshotFreshnessColor;
        private set => SetProperty(ref _snapshotFreshnessColor, value);
    }

    public bool SnapshotAutoRefreshEnabled
    {
        get => _snapshotAutoRefreshEnabled;
        set
        {
            if (SetProperty(ref _snapshotAutoRefreshEnabled, value))
            {
                OnPropertyChanged(nameof(SnapshotRefreshModeLabel));
                UpdateSnapshotFreshness();
                AddLog(value ? "Snapshot auto-refresh enabled." : "Snapshot refresh set to manual.");
            }
        }
    }

    public string SnapshotRefreshModeLabel => SnapshotAutoRefreshEnabled ? "Auto refresh on" : "Auto refresh off";

    public string HeaderPowerActionToolTip =>
        _currentSnapshot?.IsAwake == true
            ? "Request headset sleep"
            : "Request headset wake";

    public string HeaderProximityActionToolTip =>
        _currentProximityStatus?.HoldActive == true
            ? "Restore normal proximity behavior"
            : "Request keep-awake proximity hold";

    public string SelectedSerial
    {
        get => _selectedSerial;
        set
        {
            if (SetProperty(ref _selectedSerial, value))
            {
                ResetHeaderSnapshotStatus();
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
                ApplySelectedCatalogAppToFields();
                RefreshRuntimeProfilesForSelectedApp();
                UseCatalogAppCommand.RaiseCanExecuteChanged();
                InstallCatalogAppCommand.RaiseCanExecuteChanged();
                LaunchCatalogAppCommand.RaiseCanExecuteChanged();
                VerifyCatalogAppCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RuntimeProfile? SelectedRuntimeProfile
    {
        get => _selectedRuntimeProfile;
        set
        {
            if (SetProperty(ref _selectedRuntimeProfile, value))
            {
                RuntimeProfileId = value?.Id ?? string.Empty;
                OnPropertyChanged(nameof(SelectedRuntimeProfileDescription));
            }
        }
    }

    public string SelectedRuntimeProfileDescription =>
        SelectedRuntimeProfile?.Description ?? "No launch mode selected. Launch will use the app's default behavior.";

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

    public string LastProximityStatus
    {
        get => _lastProximityStatus;
        set => SetProperty(ref _lastProximityStatus, value);
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
    public AsyncRelayCommand SnapshotFreshnessCommand { get; }
    public AsyncRelayCommand BrowseCatalogCommand { get; }
    public AsyncRelayCommand LoadCatalogCommand { get; }
    public AsyncRelayCommand UseCatalogAppCommand { get; }
    public AsyncRelayCommand InstallCatalogAppCommand { get; }
    public AsyncRelayCommand LaunchCatalogAppCommand { get; }
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
    public AsyncRelayCommand ToggleProximityCommand { get; }
    public AsyncRelayCommand TogglePowerCommand { get; }
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
        if (SnapshotAutoRefreshEnabled && HasSerial())
        {
            await RefreshSnapshotFromHeaderAsync(autoTriggered: true).ConfigureAwait(true);
        }
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

            RefreshHeaderDeviceStatus();
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
        await RefreshSnapshotFromHeaderAsync(autoTriggered: false).ConfigureAwait(true);
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
        await UseCatalogAppAsync().ConfigureAwait(true);
    }

    private Task UseCatalogAppAsync()
    {
        if (SelectedCatalogApp is null)
        {
            return Task.CompletedTask;
        }

        ApplySelectedCatalogAppToFields();
        AddLog($"Using catalog app: {SelectedCatalogApp.Label}");
        return Task.CompletedTask;
    }

    private void ApplySelectedCatalogAppToFields()
    {
        if (SelectedCatalogApp is null)
        {
            return;
        }

        PackageName = SelectedCatalogApp.PackageName;
        ActivityName = SelectedCatalogApp.ActivityName ?? string.Empty;
        ApkPath = CatalogLoader.ResolveApkPath(CatalogPath, SelectedCatalogApp) ?? ApkPath;
    }

    private void RefreshRuntimeProfilesForSelectedApp()
    {
        RuntimeProfiles.Clear();
        if (_catalog is null || SelectedCatalogApp is null)
        {
            SelectedRuntimeProfile = null;
            return;
        }

        var matchingProfiles = _catalog.RuntimeProfiles
            .Where(profile => RuntimeProfileBelongsToApp(SelectedCatalogApp, profile))
            .ToArray();
        if (matchingProfiles.Length == 0)
        {
            matchingProfiles = _catalog.RuntimeProfiles.ToArray();
        }

        foreach (var profile in matchingProfiles)
        {
            RuntimeProfiles.Add(profile);
        }

        SelectedRuntimeProfile =
            RuntimeProfiles.FirstOrDefault(profile => string.Equals(profile.Id, RuntimeProfileId, StringComparison.OrdinalIgnoreCase)) ??
            RuntimeProfiles.FirstOrDefault(profile => string.Equals(profile.Id, CompanionContentLayout.DefaultRuntimeProfileId, StringComparison.OrdinalIgnoreCase)) ??
            RuntimeProfiles.FirstOrDefault();
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

    private async Task InstallCatalogAppAsync()
    {
        await RunUiActionAsync("Installing selected catalog APK...", async () =>
        {
            if (SelectedCatalogApp is null)
            {
                throw new InvalidOperationException("Load a catalog and select an app first.");
            }

            ApplySelectedCatalogAppToFields();
            var apkPath = ResolveSelectedApkPathForInstall();
            var result = await _adbService.InstallAsync(SelectedSerial, apkPath).ConfigureAwait(true);
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

    private async Task LaunchCatalogAppAsync()
    {
        await RunUiActionAsync("Launching selected catalog mode...", async () =>
        {
            if (SelectedCatalogApp is null)
            {
                throw new InvalidOperationException("Load a catalog and select an app first.");
            }

            ApplySelectedCatalogAppToFields();
            var runtimeProfile = SelectedRuntimeProfile;
            if (runtimeProfile is not null)
            {
                AddLog($"Launching mode: {runtimeProfile.Label}");
            }

            var result = await _adbService.LaunchAsync(
                SelectedSerial,
                PackageName,
                string.IsNullOrWhiteSpace(ActivityName) ? null : ActivityName,
                runtimeProfile?.Values).ConfigureAwait(true);
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

            var runtimeProfile = SelectedRuntimeProfile ?? ResolveRuntimeProfile(_catalog, RuntimeProfileId);
            if (runtimeProfile is not null)
            {
                AddLog($"Using runtime profile: {runtimeProfile.Label}");
            }

            var install = await _adbService.InstallAsync(SelectedSerial, ResolveSelectedApkPathForInstall()).ConfigureAwait(true);
            AddLog(install.CondensedOutput);

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
            ApplySnapshot(snapshot);

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
        await SetProximityModeAsync(enableNormalProximity: false).ConfigureAwait(true);
    }

    private async Task RestoreProximityAsync()
    {
        await SetProximityModeAsync(enableNormalProximity: true).ConfigureAwait(true);
    }

    private async Task ReadProximityAsync()
    {
        HeaderProximityStatus = "Proximity requested";
        HeaderProximityStatusColor = StatusWarningColor;
        var succeeded = await RunUiActionAsync("Reading proximity status...", ReadProximityCoreAsync).ConfigureAwait(true);
        if (!succeeded)
        {
            HeaderProximityStatusColor = StatusErrorColor;
        }
    }

    private async Task WakeHeadsetAsync()
    {
        await SetHeadsetPowerAsync(requestSleep: false).ConfigureAwait(true);
    }

    private async Task TogglePowerAsync()
    {
        await SetHeadsetPowerAsync(requestSleep: _currentSnapshot?.IsAwake == true).ConfigureAwait(true);
    }

    private async Task ToggleProximityAsync()
    {
        await SetProximityModeAsync(enableNormalProximity: _currentProximityStatus?.HoldActive == true).ConfigureAwait(true);
    }

    private async Task SetHeadsetPowerAsync(bool requestSleep)
    {
        HeaderPowerStatus = requestSleep ? "Sleep requested" : "Wake requested";
        HeaderPowerStatusColor = StatusWarningColor;

        var succeeded = await RunUiActionAsync(
            requestSleep ? "Requesting headset sleep..." : "Sending hzdb wake request...",
            async () =>
            {
                var result = requestSleep
                    ? await _adbService.SleepDeviceAsync(SelectedSerial).ConfigureAwait(true)
                    : await _hzdbService.WakeDeviceAsync(SelectedSerial).ConfigureAwait(true);
                AddLog(result.CondensedOutput.Length > 0
                    ? result.CondensedOutput
                    : requestSleep
                        ? "Sleep request sent."
                        : "Wake request sent.");
                await RefreshSnapshotCoreAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);

        if (!succeeded)
        {
            HeaderPowerStatusColor = StatusErrorColor;
        }
    }

    private async Task SetProximityModeAsync(bool enableNormalProximity)
    {
        HeaderProximityStatus = enableNormalProximity ? "Normal requested" : "Keep-awake requested";
        HeaderProximityStatusColor = StatusWarningColor;

        var succeeded = await RunUiActionAsync(
            enableNormalProximity
                ? "Restoring normal proximity behavior..."
                : "Disabling wear sensor for keep-awake...",
            async () =>
            {
                var durationMs = int.TryParse(ProximityDurationMs, out var parsedDuration) ? parsedDuration : 28_800_000;
                var result = await _hzdbService
                    .SetProximityAsync(SelectedSerial, enableNormalProximity, enableNormalProximity ? null : durationMs)
                    .ConfigureAwait(true);
                AddLog(result.CondensedOutput.Length > 0
                    ? result.CondensedOutput
                    : enableNormalProximity
                        ? "Normal proximity behavior requested."
                        : $"Keep-awake proximity hold requested for {durationMs} ms.");
                await ReadProximityCoreAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);

        if (!succeeded)
        {
            HeaderProximityStatusColor = StatusErrorColor;
        }
    }

    private async Task ReadProximityCoreAsync()
    {
        var status = await _hzdbService.GetProximityStatusAsync(SelectedSerial).ConfigureAwait(true);
        LastProximityStatus =
            $"{status.Detail}{Environment.NewLine}" +
            $"Virtual state: {status.VirtualState}{Environment.NewLine}" +
            $"Autosleep disabled: {status.IsAutosleepDisabled}{Environment.NewLine}" +
            $"Headset state: {status.HeadsetState}{Environment.NewLine}" +
            $"Auto sleep: {FormatMilliseconds(status.AutoSleepTimeMs)}{Environment.NewLine}" +
            $"Hold until: {status.HoldUntil?.ToString("G") ?? "unknown"}";
        ApplyHeaderProximityStatus(status);
        AddLog(status.Detail);
    }

    private async Task RefreshSnapshotCoreAsync()
    {
        var snapshot = await _adbService.GetSnapshotAsync(SelectedSerial).ConfigureAwait(true);
        ApplySnapshot(snapshot);
        AddLog("Headset status refreshed.");
    }

    private void ApplySnapshot(QuestSnapshot snapshot)
    {
        _currentSnapshot = snapshot;
        _lastSnapshotAt = snapshot.CapturedAt;
        _lastSnapshotRefreshFailed = false;
        _lastSnapshotRefreshFailure = string.Empty;
        LastSnapshot =
            $"Serial: {snapshot.Serial}{Environment.NewLine}" +
            $"Model: {snapshot.Model}{Environment.NewLine}" +
            $"Headset battery: {snapshot.Battery}{Environment.NewLine}" +
            $"Power: {FormatPower(snapshot)}{Environment.NewLine}" +
            $"Foreground: {snapshot.Foreground}{Environment.NewLine}" +
            $"Shell focus: {snapshot.ForegroundStatus?.Detail ?? "not checked"}{Environment.NewLine}" +
            $"Captured: {snapshot.CapturedAt:t}";

        HeaderDeviceStatus = FormatHeaderDeviceStatus(snapshot.Serial, snapshot.Model);
        HeaderHeadsetBatteryStatus = FormatHeaderBattery(snapshot);
        HeaderPowerStatus = FormatHeaderPower(snapshot);
        HeaderControllerStatus = FormatHeaderControllers(snapshot.Controllers);
        HeaderForegroundStatus = FormatHeaderForeground(snapshot);
        HeaderDeviceStatusColor = StatusOkColor;
        HeaderHeadsetBatteryStatusColor = FormatHeaderBatteryColor(snapshot);
        HeaderPowerStatusColor = FormatHeaderPowerColor(snapshot);
        HeaderControllerStatusColor = FormatHeaderControllersColor(snapshot.Controllers);
        HeaderForegroundStatusColor = FormatHeaderForegroundColor(snapshot);

        ControllerStatuses.Clear();
        foreach (var controller in snapshot.Controllers ?? Array.Empty<QuestControllerStatus>())
        {
            ControllerStatuses.Add(controller);
        }

        if (snapshot.Proximity is not null)
        {
            LastProximityStatus =
                $"{snapshot.Proximity.Detail}{Environment.NewLine}" +
                $"Virtual state: {snapshot.Proximity.VirtualState}{Environment.NewLine}" +
                $"Autosleep disabled: {snapshot.Proximity.IsAutosleepDisabled}{Environment.NewLine}" +
                $"Headset state: {snapshot.Proximity.HeadsetState}{Environment.NewLine}" +
                $"Auto sleep: {FormatMilliseconds(snapshot.Proximity.AutoSleepTimeMs)}{Environment.NewLine}" +
                $"Hold until: {snapshot.Proximity.HoldUntil?.ToString("G") ?? "unknown"}";
            ApplyHeaderProximityStatus(snapshot.Proximity);
        }
        else
        {
            LastProximityStatus = "Proximity status was not reported by this snapshot. Use Read Proximity Status for direct readback.";
            HeaderProximityStatus = "Proximity --";
            HeaderProximityStatusColor = StatusWarningColor;
            _currentProximityStatus = null;
        }

        UpdateSnapshotFreshness();
        OnPropertyChanged(nameof(HeaderPowerActionToolTip));
        OnPropertyChanged(nameof(HeaderProximityActionToolTip));
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

    private async Task<bool> RunUiActionAsync(string runningStatus, Func<Task> action)
    {
        try
        {
            Status = runningStatus;
            await action().ConfigureAwait(true);
            Status = "Ready.";
            return true;
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            AddLog($"Error: {exception.Message}");
            return false;
        }
    }

    private bool HasSerial() => !string.IsNullOrWhiteSpace(SelectedSerial);
    private bool HasCatalogApp() => SelectedCatalogApp is not null;
    private bool HasSerialAndCatalogApp() => HasSerial() && HasCatalogApp();

    private string ResolveSelectedApkPathForInstall()
    {
        if (string.IsNullOrWhiteSpace(ApkPath) || !File.Exists(ApkPath))
        {
            throw new InvalidOperationException("The selected catalog APK is not available on disk.");
        }

        return ApkPath;
    }

    private static bool RuntimeProfileBelongsToApp(QuestAppTarget app, RuntimeProfile profile)
    {
        if (!profile.Values.TryGetValue("rustyxr.example", out var example))
        {
            return false;
        }

        return (string.Equals(app.Id, "rusty-xr-quest-composite-layer", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(example, "quest-composite-layer-apk", StringComparison.OrdinalIgnoreCase)) ||
               (string.Equals(app.Id, "rusty-xr-quest-minimal", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(example, "quest-minimal-apk", StringComparison.OrdinalIgnoreCase));
    }

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

    private void ResetHeaderSnapshotStatus()
    {
        _currentSnapshot = null;
        _currentProximityStatus = null;
        _lastSnapshotAt = null;
        _lastSnapshotAttemptAt = null;
        _lastSnapshotRefreshFailed = false;
        _lastSnapshotRefreshFailure = string.Empty;
        RefreshHeaderDeviceStatus();
        HeaderHeadsetBatteryStatus = "Battery --";
        HeaderPowerStatus = "Power --";
        HeaderControllerStatus = "Controllers --";
        HeaderProximityStatus = "Proximity --";
        HeaderForegroundStatus = "Foreground --";
        HeaderHeadsetBatteryStatusColor = StatusWarningColor;
        HeaderPowerStatusColor = StatusWarningColor;
        HeaderControllerStatusColor = StatusWarningColor;
        HeaderProximityStatusColor = StatusWarningColor;
        HeaderForegroundStatusColor = StatusWarningColor;
        ControllerStatuses.Clear();
        UpdateSnapshotFreshness();
        OnPropertyChanged(nameof(HeaderPowerActionToolTip));
        OnPropertyChanged(nameof(HeaderProximityActionToolTip));
    }

    private void RefreshHeaderDeviceStatus()
    {
        if (string.IsNullOrWhiteSpace(SelectedSerial))
        {
            HeaderDeviceStatus = "No Quest selected";
            HeaderDeviceStatusColor = StatusErrorColor;
            return;
        }

        var selected = Devices.FirstOrDefault(device =>
            string.Equals(device.Serial, SelectedSerial, StringComparison.OrdinalIgnoreCase));
        HeaderDeviceStatus = FormatHeaderDeviceStatus(SelectedSerial, selected?.Model);
        HeaderDeviceStatusColor = selected is null
            ? StatusWarningColor
            : selected.IsOnline
                ? StatusOkColor
                : StatusErrorColor;
    }

    private void ApplyHeaderProximityStatus(QuestProximityStatus status)
    {
        _currentProximityStatus = status;
        if (!status.Available)
        {
            HeaderProximityStatus = "Proximity unavailable";
            HeaderProximityStatusColor = StatusErrorColor;
            OnPropertyChanged(nameof(HeaderProximityActionToolTip));
            return;
        }

        if (status.HoldActive)
        {
            HeaderProximityStatus = "Proximity keep-awake";
            HeaderProximityStatusColor = StatusOkColor;
            OnPropertyChanged(nameof(HeaderProximityActionToolTip));
            return;
        }

        HeaderProximityStatus = string.IsNullOrWhiteSpace(status.HeadsetState)
            ? "Proximity normal"
            : $"Proximity {FormatCompactState(status.HeadsetState)}";
        HeaderProximityStatusColor = StatusOkColor;
        OnPropertyChanged(nameof(HeaderProximityActionToolTip));
    }

    private async Task RefreshSnapshotFromHeaderAsync(bool autoTriggered)
    {
        if (!HasSerial() || _snapshotRefreshInFlight)
        {
            return;
        }

        _snapshotRefreshInFlight = true;
        _lastSnapshotRefreshFailed = false;
        _lastSnapshotRefreshFailure = string.Empty;
        _lastSnapshotAttemptAt = DateTimeOffset.Now;
        SnapshotFreshnessText = "requested";
        SnapshotFreshnessDetail = "Snapshot refresh requested; waiting for ADB readback.";
        SnapshotFreshnessColor = StatusWarningColor;
        RaiseCommandStates();

        try
        {
            var succeeded = await RunUiActionAsync(
                autoTriggered ? "Auto-refreshing headset status..." : "Refreshing headset status...",
                RefreshSnapshotCoreAsync).ConfigureAwait(true);
            if (!succeeded)
            {
                _lastSnapshotRefreshFailed = true;
                _lastSnapshotRefreshFailure = Status;
            }
        }
        finally
        {
            _snapshotRefreshInFlight = false;
            UpdateSnapshotFreshness();
            RaiseCommandStates();
        }
    }

    private async Task OnSnapshotTimerTickAsync()
    {
        UpdateSnapshotFreshness();
        if (!SnapshotAutoRefreshEnabled || !HasSerial() || _snapshotRefreshInFlight)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var lastActivity = _lastSnapshotAt ?? _lastSnapshotAttemptAt;
        if (lastActivity is not null && now - lastActivity.Value < AutoSnapshotInterval)
        {
            return;
        }

        await RefreshSnapshotFromHeaderAsync(autoTriggered: true).ConfigureAwait(true);
    }

    private void UpdateSnapshotFreshness()
    {
        if (_snapshotRefreshInFlight)
        {
            SnapshotFreshnessColor = StatusWarningColor;
            return;
        }

        if (_lastSnapshotRefreshFailed)
        {
            var ageDetail = _lastSnapshotAt is null
                ? "No confirmed snapshot is available."
                : $"Latest snapshot age {FormatAge(DateTimeOffset.Now - _lastSnapshotAt.Value)}.";
            SnapshotFreshnessText = "refresh failed";
            SnapshotFreshnessDetail = $"{_lastSnapshotRefreshFailure} {ageDetail}".Trim();
            SnapshotFreshnessColor = StatusErrorColor;
            return;
        }

        if (_lastSnapshotAt is null)
        {
            SnapshotFreshnessText = "never";
            SnapshotFreshnessDetail = "No headset snapshot captured yet.";
            SnapshotFreshnessColor = StatusWarningColor;
            return;
        }

        var age = DateTimeOffset.Now - _lastSnapshotAt.Value;
        var ageText = FormatAge(age);
        SnapshotFreshnessText = ageText == "now" ? "now" : $"{ageText} ago";
        SnapshotFreshnessDetail =
            $"Latest snapshot captured at {_lastSnapshotAt.Value.LocalDateTime:G}; age {ageText}.";
        SnapshotFreshnessColor = age <= SnapshotFreshDuration
            ? StatusOkColor
            : StatusErrorColor;
    }

    private static string FormatHeaderDeviceStatus(string serial, string? model)
    {
        var device = string.IsNullOrWhiteSpace(model)
            ? "Quest"
            : model.Replace('_', ' ');
        if (string.IsNullOrWhiteSpace(serial))
        {
            return device;
        }

        var suffix = serial.Length <= 4 ? serial : serial[^4..];
        return $"{device} {suffix}";
    }

    private static string FormatHeaderBattery(QuestSnapshot snapshot)
    {
        if (snapshot.HeadsetBatteryLevel is int level)
        {
            var status = string.IsNullOrWhiteSpace(snapshot.HeadsetBatteryStatus) ||
                         string.Equals(snapshot.HeadsetBatteryStatus, "unknown", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $" {snapshot.HeadsetBatteryStatus}";
            return $"{level}%{status}";
        }

        return string.IsNullOrWhiteSpace(snapshot.Battery) || string.Equals(snapshot.Battery, "unknown", StringComparison.OrdinalIgnoreCase)
            ? "Battery --"
            : snapshot.Battery;
    }

    private static string FormatHeaderPower(QuestSnapshot snapshot)
    {
        var state = snapshot.IsAwake switch
        {
            true => "Awake",
            false => "Asleep",
            _ => string.IsNullOrWhiteSpace(snapshot.Wakefulness) ? "Power --" : snapshot.Wakefulness
        };

        return string.IsNullOrWhiteSpace(snapshot.DisplayPowerState)
            ? state
            : $"{state} / {snapshot.DisplayPowerState}";
    }

    private static string FormatHeaderControllers(IReadOnlyList<QuestControllerStatus>? controllers)
    {
        if (controllers is null || controllers.Count == 0)
        {
            return "Controllers --";
        }

        return string.Join("; ", controllers.Select(static controller =>
        {
            var hand = controller.HandLabel.StartsWith("Left", StringComparison.OrdinalIgnoreCase)
                ? "L"
                : controller.HandLabel.StartsWith("Right", StringComparison.OrdinalIgnoreCase)
                    ? "R"
                    : controller.HandLabel;
            var connection = FormatCompactState(controller.ConnectionState);
            return string.IsNullOrWhiteSpace(connection)
                ? $"{hand} {controller.BatteryLabel}"
                : $"{hand} {controller.BatteryLabel} {connection}";
        }));
    }

    private static string FormatHeaderForeground(QuestSnapshot snapshot)
    {
        if (snapshot.ForegroundStatus?.HasKnownBlocker == true)
        {
            return $"Blocked: {snapshot.ForegroundStatus.BlockerLabel}";
        }

        if (snapshot.ForegroundStatus?.FocusDiffersFromResumed == true)
        {
            return "Focus differs";
        }

        var foreground = snapshot.Foreground;
        return string.IsNullOrWhiteSpace(foreground) || string.Equals(foreground, "unknown", StringComparison.OrdinalIgnoreCase)
            ? "Foreground --"
            : $"Foreground: {foreground}";
    }

    private static string FormatHeaderBatteryColor(QuestSnapshot snapshot)
    {
        if (snapshot.HeadsetBatteryLevel is not int level)
        {
            return StatusWarningColor;
        }

        if (snapshot.HeadsetBatteryStatus.Contains("charging", StringComparison.OrdinalIgnoreCase) ||
            snapshot.HeadsetBatteryStatus.Contains("full", StringComparison.OrdinalIgnoreCase))
        {
            return StatusOkColor;
        }

        return level <= 20 ? StatusErrorColor : StatusOkColor;
    }

    private static string FormatHeaderPowerColor(QuestSnapshot snapshot)
    {
        return snapshot.IsAwake switch
        {
            true => StatusOkColor,
            false => StatusErrorColor,
            _ => StatusWarningColor
        };
    }

    private static string FormatHeaderControllersColor(IReadOnlyList<QuestControllerStatus>? controllers)
    {
        if (controllers is null || controllers.Count == 0)
        {
            return StatusWarningColor;
        }

        if (controllers.Any(static controller =>
                controller.BatteryLevel is <= 10 ||
                controller.ConnectionState.Contains("DISCONNECTED", StringComparison.OrdinalIgnoreCase) ||
                controller.ConnectionState.Contains("OFF", StringComparison.OrdinalIgnoreCase)))
        {
            return StatusErrorColor;
        }

        if (controllers.Any(static controller =>
                controller.BatteryLevel is null ||
                string.IsNullOrWhiteSpace(controller.ConnectionState)))
        {
            return StatusWarningColor;
        }

        return StatusOkColor;
    }

    private static string FormatHeaderForegroundColor(QuestSnapshot snapshot)
    {
        if (snapshot.ForegroundStatus?.HasKnownBlocker == true)
        {
            return StatusErrorColor;
        }

        if (snapshot.ForegroundStatus?.FocusDiffersFromResumed == true)
        {
            return StatusWarningColor;
        }

        var foreground = snapshot.Foreground;
        return string.IsNullOrWhiteSpace(foreground) || string.Equals(foreground, "unknown", StringComparison.OrdinalIgnoreCase)
            ? StatusWarningColor
            : StatusOkColor;
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age.TotalSeconds < 1)
        {
            return "now";
        }

        if (age.TotalSeconds < 60)
        {
            return $"{Math.Round(age.TotalSeconds):0}s";
        }

        if (age.TotalMinutes < 60)
        {
            return $"{Math.Round(age.TotalMinutes):0}m";
        }

        return $"{Math.Round(age.TotalHours):0}h";
    }

    private static string FormatCompactState(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace("CONNECTED_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("HEADSET_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace('_', ' ')
            .ToLowerInvariant();
    }

    private static string FormatPower(QuestSnapshot snapshot)
    {
        var awake = snapshot.IsAwake switch
        {
            true => "awake",
            false => "asleep",
            _ => "unknown"
        };

        var parts = new[]
        {
            awake,
            string.IsNullOrWhiteSpace(snapshot.Wakefulness) ? null : $"wakefulness {snapshot.Wakefulness}",
            snapshot.IsInteractive is null ? null : $"interactive {snapshot.IsInteractive.Value.ToString().ToLowerInvariant()}",
            string.IsNullOrWhiteSpace(snapshot.DisplayPowerState) ? null : $"display {snapshot.DisplayPowerState}"
        };

        return string.Join("; ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string FormatMilliseconds(int? milliseconds)
    {
        if (milliseconds is null)
        {
            return "unknown";
        }

        return milliseconds.Value >= 1000
            ? $"{milliseconds.Value / 1000d:0.#}s"
            : $"{milliseconds.Value}ms";
    }

    private void RaiseCommandStates()
    {
        EnableWifiAdbCommand.RaiseCanExecuteChanged();
        SnapshotCommand.RaiseCanExecuteChanged();
        SnapshotFreshnessCommand.RaiseCanExecuteChanged();
        InstallCatalogAppCommand.RaiseCanExecuteChanged();
        LaunchCatalogAppCommand.RaiseCanExecuteChanged();
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
        ToggleProximityCommand.RaiseCanExecuteChanged();
        TogglePowerCommand.RaiseCanExecuteChanged();
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
