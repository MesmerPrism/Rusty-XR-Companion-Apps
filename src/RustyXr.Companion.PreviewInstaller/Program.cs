using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Principal;

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
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new InstallerForm(InstallAsync, ReleasePageUrl);
        Application.Run(form);
        return form.ExitCode;
    }

    private static async Task<string> InstallAsync(IProgress<InstallerProgress> progress, CancellationToken cancellationToken)
    {
        progress.Report(new InstallerProgress("Preparing install", "Creating a temporary download folder.", 5));
        var tempRoot = Path.Combine(Path.GetTempPath(), "RustyXrCompanionSetup");
        var zipPath = Path.Combine(tempRoot, "RustyXrCompanion-win-x64.zip");
        var installRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "RustyXrCompanion");

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

        progress.Report(new InstallerProgress("Creating shortcut", "Creating a Start Menu shortcut for the installed app.", 85));
        CreateShortcut(installRoot);

        progress.Report(new InstallerProgress("Launching app", "Opening Rusty XR Companion.", 95));
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

        progress.Report(new InstallerProgress("Installed", $"Rusty XR Companion is installed at {installRoot}.", 100));
        return installRoot;
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

    private static void CreateShortcut(string installRoot)
    {
        var shortcutDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            "Rusty XR Companion");
        Directory.CreateDirectory(shortcutDirectory);

        var shortcutPath = Path.Combine(shortcutDirectory, "Rusty XR Companion.url");
        var exePath = Path.Combine(installRoot, AppExeName).Replace("\\", "/", StringComparison.Ordinal);
        File.WriteAllText(shortcutPath, $"[InternetShortcut]{Environment.NewLine}URL=file:///{exePath}{Environment.NewLine}");
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

internal sealed class InstallerForm : Form
{
    private readonly Func<IProgress<InstallerProgress>, CancellationToken, Task<string>> _install;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly string _releasePageUrl;
    private readonly Label _status = new() { AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
    private readonly Label _detail = new() { AutoSize = false, Height = 70, Width = 520 };
    private readonly ProgressBar _progress = new() { Width = 520, Height = 24 };
    private readonly Button _installButton = new() { Text = "Install latest release", Width = 160 };
    private readonly Button _releaseButton = new() { Text = "Open release page", Width = 150 };

    public InstallerForm(
        Func<IProgress<InstallerProgress>, CancellationToken, Task<string>> install,
        string releasePageUrl)
    {
        _install = install;
        _releasePageUrl = releasePageUrl;
        ExitCode = 1;

        Text = "Rusty XR Companion Setup";
        Width = 600;
        Height = 260;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(24),
            WrapContents = false
        };

        _status.Text = "Install Rusty XR Companion";
        _detail.Text = "This helper installs the latest portable Windows release into your user profile and creates a Start Menu shortcut.";
        _installButton.Click += async (_, _) => await RunInstallAsync().ConfigureAwait(true);
        _releaseButton.Click += (_, _) => Process.Start(new ProcessStartInfo(_releasePageUrl) { UseShellExecute = true });

        var buttons = new FlowLayoutPanel { Width = 520, Height = 42, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(_installButton);
        buttons.Controls.Add(_releaseButton);

        layout.Controls.Add(_status);
        layout.Controls.Add(_detail);
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

    private async Task RunInstallAsync()
    {
        _installButton.Enabled = false;
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
            _detail.Text = $"Installed at {installRoot}. You can close this window.";
        }
        catch (Exception exception)
        {
            ExitCode = 1;
            _status.Text = "Install failed";
            _detail.Text = exception.Message;
            _installButton.Enabled = true;
        }
    }
}
