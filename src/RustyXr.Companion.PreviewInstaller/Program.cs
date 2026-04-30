using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using RustyXr.Companion.Core;
using RustyXr.Companion.Windows;

namespace RustyXr.Companion.PreviewInstaller;

internal readonly record struct InstallerProgress(string Status, string Detail, int Percent);

internal static class Program
{
    private const string ReleaseZipUrl =
        "https://github.com/MesmerPrism/Rusty-XR-Companion-Apps/releases/latest/download/RustyXrCompanion-win-x64.zip";
    private const string ReleasePageUrl =
        "https://github.com/MesmerPrism/Rusty-XR-Companion-Apps/releases";
    private const string AppExeName = "RustyXr.Companion.App.exe";

    [STAThread]
    private static int Main(string[] args)
    {
        var options = InstallerOptions.Parse(args);
        if (options.QuietUninstall)
        {
            try
            {
                PortableInstallUninstaller.StartReleaseUninstall(options.PurgeData);
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        using var form = new InstallerForm(
            InstallAsync,
            UninstallAsync,
            ReleasePageUrl,
            options.Uninstall,
            options.PurgeData);
        Application.Run(form);
        return form.ExitCode;
    }

    private static async Task<string> InstallAsync(IProgress<InstallerProgress> progress, CancellationToken cancellationToken)
    {
        progress.Report(new InstallerProgress(
            "Preparing install",
            $"App files will be installed at {PortableInstallLayout.ReleaseInstallRoot()}. The launcher icon will be placed at {PortableInstallLayout.ReleaseShortcutDisplayPath()}.",
            5));
        var tempRoot = Path.Combine(Path.GetTempPath(), "RustyXrCompanionSetup");
        var zipPath = Path.Combine(tempRoot, "RustyXrCompanion-win-x64.zip");
        var installRoot = PortableInstallLayout.ReleaseInstallRoot();

        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(installRoot);

        progress.Report(new InstallerProgress("Downloading release", "Fetching the latest portable app zip from GitHub Releases.", 25));
        using var http = new HttpClient();
        await using (var output = File.Create(zipPath))
        using (var response = await http.GetAsync(ReleaseZipUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true))
        {
            response.EnsureSuccessStatusCode();
            await response.Content.CopyToAsync(output, cancellationToken).ConfigureAwait(true);
        }

        progress.Report(new InstallerProgress("Replacing installed files", "Extracting the portable app into the per-user Programs folder.", 60));
        var staging = installRoot + ".staging";
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        ZipFile.ExtractToDirectory(zipPath, staging);

        foreach (var file in Directory.EnumerateFiles(installRoot, "*", SearchOption.AllDirectories))
        {
            TryDelete(file);
        }

        foreach (var directory in Directory.EnumerateDirectories(installRoot).OrderByDescending(static path => path.Length))
        {
            TryDeleteDirectory(directory);
        }

        CopyDirectory(staging, installRoot);
        Directory.Delete(staging, recursive: true);

        progress.Report(new InstallerProgress(
            "Creating shortcuts",
            $"Creating the Start Menu launcher icon at {PortableInstallLayout.ReleaseShortcutDisplayPath()} and the Windows uninstall entry.",
            82));
        PortableInstallRegistration.EnsureInstalledUninstaller(installRoot, Environment.ProcessPath);
        PortableInstallRegistration.CreateReleaseShortcut(installRoot);
        PortableInstallRegistration.RegisterReleaseInstall(installRoot);

        progress.Report(new InstallerProgress("Refreshing Quest tooling", "Installing or updating managed hzdb, Android platform-tools, and scrcpy.", 88));
        try
        {
            using var tooling = new OfficialQuestToolingService();
            var toolingProgress = new Progress<OfficialQuestToolingProgress>(update =>
                progress.Report(new InstallerProgress(
                    update.Status,
                    update.Detail,
                    Math.Clamp(88 + update.PercentComplete / 8, 88, 98))));
            await tooling.InstallOrUpdateAsync(toolingProgress, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            progress.Report(new InstallerProgress(
                "Quest tooling refresh skipped",
                $"The app was installed, but the managed tool cache could not be refreshed now: {exception.Message}",
                94));
        }

        progress.Report(new InstallerProgress("Launching app", "Opening Rusty XR Companion.", 98));
        var exePath = Path.Combine(installRoot, AppExeName);
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("The published app executable was not found in the release zip.", exePath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = installRoot,
            UseShellExecute = true
        });

        progress.Report(new InstallerProgress(
            "Installed",
            $"Rusty XR Companion is installed at {installRoot}. The launcher icon is at {PortableInstallLayout.ReleaseShortcutDisplayPath()}.",
            100));
        return installRoot;
    }

    private static Task<PortableUninstallLaunch> UninstallAsync(
        IProgress<InstallerProgress> progress,
        bool purgeData,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uninstallProgress = new Progress<PortableInstallProgress>(update =>
            progress.Report(new InstallerProgress(update.Status, update.Detail, update.Percent)));
        var launch = PortableInstallUninstaller.StartReleaseUninstall(purgeData, uninstallProgress);
        return Task.FromResult(launch);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, destination, StringComparison.OrdinalIgnoreCase), overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Keep installing over files that are not currently locked.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path);
        }
        catch
        {
            // A locked directory will be replaced on the next install.
        }
    }
}

internal readonly record struct InstallerOptions(bool Uninstall, bool QuietUninstall, bool PurgeData)
{
    public static InstallerOptions Parse(IEnumerable<string> args)
    {
        var uninstall = false;
        var quietUninstall = false;
        var purgeData = false;

        foreach (var arg in args)
        {
            switch (arg.Trim().ToLowerInvariant())
            {
                case "--uninstall":
                case "/uninstall":
                    uninstall = true;
                    break;
                case "--quiet-uninstall":
                case "/quiet-uninstall":
                    quietUninstall = true;
                    uninstall = true;
                    break;
                case "--purge-data":
                case "/purge-data":
                    purgeData = true;
                    break;
            }
        }

        return new InstallerOptions(uninstall, quietUninstall, purgeData);
    }
}

internal sealed class InstallerForm : Form
{
    private readonly Func<IProgress<InstallerProgress>, CancellationToken, Task<string>> _install;
    private readonly Func<IProgress<InstallerProgress>, bool, CancellationToken, Task<PortableUninstallLaunch>> _uninstall;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Icon? _formIcon;
    private readonly string _releasePageUrl;
    private readonly Label _status = new() { AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) };
    private readonly Label _detail = new() { AutoSize = false, Height = 92, Width = 540, Margin = new Padding(0, 0, 0, 8) };
    private readonly Label _location = new() { AutoSize = false, Height = 76, Width = 540, Margin = new Padding(0, 0, 0, 8) };
    private readonly ProgressBar _progress = new() { Width = 540, Height = 24, Margin = new Padding(0, 10, 0, 12) };
    private readonly Button _installButton = CreateActionButton("Install latest release", 172);
    private readonly Button _uninstallButton = CreateActionButton("Uninstall", 124);
    private readonly Button _releaseButton = CreateActionButton("Open release page", 156);
    private readonly CheckBox _purgeData = new()
    {
        Text = "Also remove managed tools, diagnostics, and caches",
        Width = 520
    };

    public InstallerForm(
        Func<IProgress<InstallerProgress>, CancellationToken, Task<string>> install,
        Func<IProgress<InstallerProgress>, bool, CancellationToken, Task<PortableUninstallLaunch>> uninstall,
        string releasePageUrl,
        bool startInUninstallMode,
        bool purgeData)
    {
        _install = install;
        _uninstall = uninstall;
        _releasePageUrl = releasePageUrl;
        ExitCode = 1;

        Text = "Rusty XR Companion Setup";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 452);
        MinimumSize = new Size(660, 492);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        _formIcon = TryLoadApplicationIcon();
        if (_formIcon is not null)
        {
            Icon = _formIcon;
        }

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(24),
            WrapContents = false,
            AutoScroll = false
        };

        _status.Text = "Install Rusty XR Companion";
        _detail.Text = "This helper installs the latest portable Windows release, refreshes the managed Quest tooling cache, and creates a Start Menu launcher with the Companion icon.";
        _location.Text =
            $"Install folder:{Environment.NewLine}{PortableInstallLayout.ReleaseInstallRoot()}{Environment.NewLine}" +
            $"Launcher icon:{Environment.NewLine}{PortableInstallLayout.ReleaseShortcutDisplayPath()}";
        if (startInUninstallMode)
        {
            _status.Text = "Uninstall Rusty XR Companion";
            _detail.Text = "Remove the published release install, Start Menu shortcut, and Windows uninstall registration. Managed tools, diagnostics, and caches are kept unless selected below.";
        }

        _purgeData.Checked = purgeData;
        _installButton.Click += async (_, _) => await RunInstallAsync().ConfigureAwait(true);
        _uninstallButton.Click += async (_, _) => await RunUninstallAsync().ConfigureAwait(true);
        _releaseButton.Click += (_, _) => Process.Start(new ProcessStartInfo(_releasePageUrl) { UseShellExecute = true });

        var buttons = new FlowLayoutPanel
        {
            Width = 540,
            Height = 44,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 2, 0, 0)
        };
        buttons.Controls.Add(_installButton);
        buttons.Controls.Add(_uninstallButton);
        buttons.Controls.Add(_releaseButton);

        layout.Controls.Add(_status);
        layout.Controls.Add(_detail);
        layout.Controls.Add(_location);
        layout.Controls.Add(_purgeData);
        layout.Controls.Add(_progress);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
    }

    public int ExitCode { get; private set; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cancellation.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _formIcon?.Dispose();
            _cancellation.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task RunInstallAsync()
    {
        _installButton.Enabled = false;
        _uninstallButton.Enabled = false;
        var progress = new Progress<InstallerProgress>(update =>
        {
            _status.Text = update.Status;
            _detail.Text = update.Detail;
            _progress.Value = Math.Clamp(update.Percent, 0, 100);
        });

        try
        {
            var installRoot = await _install(progress, _cancellation.Token).ConfigureAwait(true);
            ExitCode = 0;
            _status.Text = "Install complete";
            _detail.Text = $"Installed at {installRoot}. The launcher icon is at {PortableInstallLayout.ReleaseShortcutDisplayPath()}. You can close this window.";
        }
        catch (Exception exception)
        {
            ExitCode = 1;
            _status.Text = "Install failed";
            _detail.Text = exception.Message;
            _installButton.Enabled = true;
            _uninstallButton.Enabled = true;
        }
    }

    private async Task RunUninstallAsync()
    {
        _installButton.Enabled = false;
        _uninstallButton.Enabled = false;
        var progress = new Progress<InstallerProgress>(update =>
        {
            _status.Text = update.Status;
            _detail.Text = update.Detail;
            _progress.Value = Math.Clamp(update.Percent, 0, 100);
        });

        try
        {
            var launch = await _uninstall(progress, _purgeData.Checked, _cancellation.Token).ConfigureAwait(true);
            ExitCode = 0;
            _status.Text = "Uninstall started";
            _detail.Text = $"Cleanup is running for {launch.InstallRoot}. You can close this window.";
            await Task.Delay(800).ConfigureAwait(true);
            Close();
        }
        catch (Exception exception)
        {
            ExitCode = 1;
            _status.Text = "Uninstall failed";
            _detail.Text = exception.Message;
            _installButton.Enabled = true;
            _uninstallButton.Enabled = true;
        }
    }

    private static Icon? TryLoadApplicationIcon()
    {
        try
        {
            var executablePath = Environment.ProcessPath ?? Application.ExecutablePath;
            return string.IsNullOrWhiteSpace(executablePath)
                ? null
                : Icon.ExtractAssociatedIcon(executablePath);
        }
        catch
        {
            return null;
        }
    }

    private static Button CreateActionButton(string text, int width)
        => new()
        {
            Text = text,
            Width = width,
            Height = 34,
            Margin = new Padding(0, 0, 10, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            UseCompatibleTextRendering = false
        };
}
