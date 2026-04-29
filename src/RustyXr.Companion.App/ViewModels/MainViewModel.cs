using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using RustyXr.Companion.Core;
using RustyXr.Companion.Diagnostics;

namespace RustyXr.Companion.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ToolLocator _toolLocator = new();
    private readonly QuestAdbService _adbService;
    private readonly ScrcpyService _scrcpyService;
    private readonly WindowsEnvironmentAnalyzer _analyzer;
    private readonly CatalogLoader _catalogLoader = new();

    private string _status = "Ready.";
    private string _selectedSerial = string.Empty;
    private string _endpoint = "192.168.1.2:5555";
    private string _catalogPath = Path.Combine("samples", "quest-session-kit", "apk-catalog.example.json");
    private string _apkPath = string.Empty;
    private string _packageName = "com.example.questapp";
    private string _activityName = string.Empty;
    private string _deviceProfileId = "balanced-dev";
    private string _runtimeProfileId = string.Empty;
    private string _settleMilliseconds = "2500";
    private string _cpuLevel = "2";
    private string _gpuLevel = "2";
    private string _lastSnapshot = "No headset snapshot captured yet.";
    private QuestSessionCatalog? _catalog;
    private QuestAppTarget? _selectedCatalogApp;

    public MainViewModel()
    {
        _adbService = new QuestAdbService(_toolLocator);
        _scrcpyService = new ScrcpyService(_toolLocator);
        _analyzer = new WindowsEnvironmentAnalyzer(_toolLocator, _adbService);

        RefreshToolsCommand = new AsyncRelayCommand(RefreshToolsAsync);
        RefreshDevicesCommand = new AsyncRelayCommand(RefreshDevicesAsync);
        ConnectCommand = new AsyncRelayCommand(ConnectAsync);
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
        DiagnosticsCommand = new AsyncRelayCommand(WriteDiagnosticsAsync);
    }

    public ObservableCollection<ToolStatus> Tools { get; } = new();
    public ObservableCollection<QuestDevice> Devices { get; } = new();
    public ObservableCollection<QuestAppTarget> CatalogApps { get; } = new();
    public ObservableCollection<string> Log { get; } = new();

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

    public AsyncRelayCommand RefreshToolsCommand { get; }
    public AsyncRelayCommand RefreshDevicesCommand { get; }
    public AsyncRelayCommand ConnectCommand { get; }
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
    public AsyncRelayCommand DiagnosticsCommand { get; }

    public async Task InitializeAsync()
    {
        await RefreshToolsAsync().ConfigureAwait(true);
        await RefreshDevicesAsync().ConfigureAwait(true);
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
            _catalog = await _catalogLoader.LoadAsync(CatalogPath).ConfigureAwait(true);
            CatalogApps.Clear();
            foreach (var app in _catalog.Apps)
            {
                CatalogApps.Add(app);
            }

            SelectedCatalogApp = CatalogApps.FirstOrDefault();
            AddLog($"Loaded {CatalogApps.Count} catalog app(s).");
            if (SelectedCatalogApp is not null)
            {
                await UseCatalogAppAsync().ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
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

            if (!string.IsNullOrWhiteSpace(RuntimeProfileId) &&
                !_catalog.RuntimeProfiles.Any(profile => string.Equals(profile.Id, RuntimeProfileId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Runtime profile '{RuntimeProfileId}' was not found.");
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

            var launch = await _adbService.LaunchAsync(SelectedSerial, PackageName, string.IsNullOrWhiteSpace(ActivityName) ? null : ActivityName).ConfigureAwait(true);
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

    private void RaiseCommandStates()
    {
        SnapshotCommand.RaiseCanExecuteChanged();
        VerifyCatalogAppCommand.RaiseCanExecuteChanged();
        InstallCommand.RaiseCanExecuteChanged();
        LaunchCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        ApplyProfileCommand.RaiseCanExecuteChanged();
        StartCastCommand.RaiseCanExecuteChanged();
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
