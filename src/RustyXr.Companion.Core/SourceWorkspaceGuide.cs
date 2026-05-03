namespace RustyXr.Companion.Core;

public static class SourceWorkspaceGuide
{
    public const string RustyXrRepoName = "Rusty-XR";
    public const string CompanionRepoName = "Rusty-XR-Companion-Apps";

    public static SourceWorkspaceStatus Evaluate(string? workspaceRoot = null, string? currentDirectory = null)
    {
        var root = ResolveWorkspaceRoot(workspaceRoot, currentDirectory ?? Directory.GetCurrentDirectory());
        var companionPath = Path.Combine(root, CompanionRepoName);
        var rustyXrPath = Path.Combine(root, RustyXrRepoName);

        var minimalBuildScript = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-minimal-apk",
            "tools",
            "Build-QuestMinimalApk.ps1");
        var minimalCatalog = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-minimal-apk",
            "catalog",
            "rusty-xr-quest-minimal.catalog.json");
        var minimalApk = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-minimal-apk",
            "build",
            "outputs",
            "rusty-xr-quest-minimal-debug.apk");
        var compositeBuildScript = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-composite-layer-apk",
            "tools",
            "Build-QuestCompositeLayerApk.ps1");
        var compositeCatalog = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-composite-layer-apk",
            "catalog",
            "rusty-xr-quest-composite-layer.catalog.json");
        var compositeApk = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-composite-layer-apk",
            "build",
            "outputs",
            CompanionContentLayout.CompositeQuestApkFileName);
        var brokerBuildScript = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-broker-apk",
            "tools",
            "Build-QuestBrokerApk.ps1");
        var brokerCatalog = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-broker-apk",
            "catalog",
            "rusty-xr-quest-broker.catalog.json");
        var brokerApk = Path.Combine(
            rustyXrPath,
            "examples",
            "quest-broker-apk",
            "build",
            "outputs",
            "rusty-xr-quest-broker-debug.apk");
        var companionCliProject = Path.Combine(
            companionPath,
            "src",
            "RustyXr.Companion.Cli",
            "RustyXr.Companion.Cli.csproj");

        var commands = new[]
        {
            new SourceWorkspaceCommand(
                "companion-tooling",
                "Install or update the companion-managed Quest operator tools.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- tooling install-official"),
            new SourceWorkspaceCommand(
                "devices",
                "Confirm ADB sees a trusted Quest.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- devices"),
            new SourceWorkspaceCommand(
                "build-minimal-apk",
                "Build the public Rusty XR minimal Android smoke-test APK.",
                rustyXrPath,
                @"powershell -ExecutionPolicy Bypass -File .\examples\quest-minimal-apk\tools\Build-QuestMinimalApk.ps1"),
            new SourceWorkspaceCommand(
                "verify-minimal-apk",
                "Install, launch, and capture a verification bundle for the minimal APK.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-minimal-apk\catalog\rusty-xr-quest-minimal.catalog.json --app rusty-xr-quest-minimal --serial <serial> --install --launch --device-profile perf-smoke-test --runtime-profile minimal-contract-log --settle-ms 4000 --out .\artifacts\verify"),
            new SourceWorkspaceCommand(
                "build-composite-apk",
                "Build the public Rusty XR immersive Quest composite-layer APK.",
                rustyXrPath,
                @"powershell -ExecutionPolicy Bypass -File .\examples\quest-composite-layer-apk\tools\Build-QuestCompositeLayerApk.ps1 -OpenXrLoaderPath <path-to-libopenxr_loader.so>"),
            new SourceWorkspaceCommand(
                "verify-composite-apk",
                "Install, launch, log, and capture diagnostics for the composite-layer APK.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-composite-layer-apk\catalog\rusty-xr-quest-composite-layer.catalog.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile camera-stereo-gpu-composite --settle-ms 9000 --logcat-lines 1400 --out .\artifacts\verify"),
            new SourceWorkspaceCommand(
                "verify-osc-listener",
                "Install, launch, and log the generic OSC UDP listener profile.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-composite-layer-apk\catalog\rusty-xr-quest-composite-layer.catalog.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile osc-udp-listener --settle-ms 5000 --logcat-lines 1000 --out .\artifacts\verify"),
            new SourceWorkspaceCommand(
                "build-broker-apk",
                "Build the public Rusty XR Quest broker proof-of-concept APK.",
                rustyXrPath,
                @"powershell -ExecutionPolicy Bypass -File .\examples\quest-broker-apk\tools\Build-QuestBrokerApk.ps1"),
            new SourceWorkspaceCommand(
                "verify-broker-apk",
                "Install, launch, and log the localhost broker status/WebSocket proof.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-broker-apk\catalog\rusty-xr-quest-broker.catalog.json --app rusty-xr-quest-broker --serial <serial> --stop-catalog-apps --install --launch --device-profile broker-smoke-test --runtime-profile broker-latency-websocket-lsl --settle-ms 5000 --logcat-lines 1000 --out .\artifacts\verify"),
            new SourceWorkspaceCommand(
                "verify-broker-osc-ingress",
                "Install, launch, and log the broker OSC drive ingress profile.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-broker-apk\catalog\rusty-xr-quest-broker.catalog.json --app rusty-xr-quest-broker --serial <serial> --stop-catalog-apps --install --launch --device-profile broker-smoke-test --runtime-profile broker-osc-drive-ingress --settle-ms 5000 --logcat-lines 1000 --out .\artifacts\verify"),
            new SourceWorkspaceCommand(
                "send-broker-osc-drive",
                "Send one OSC drive value to a broker running on the Quest LAN IP.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- osc send --host <quest-lan-ip> --port 9000 --address /rusty-xr/drive/radius --arg float:0.75"),
            new SourceWorkspaceCommand(
                "compare-broker-osc-routes",
                "Write a clock-aligned direct target-app OSC versus broker OSC/WebSocket comparison bundle.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- broker compare --quest-host <quest-lan-ip> --serial <serial> --count 16 --interval-ms 250 --out .\artifacts\broker-compare --json"),
            new SourceWorkspaceCommand(
                "verify-lsl-runtime",
                "Check that the companion can load a user-supplied Windows lsl.dll.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- lsl runtime --lsl-dll <path-to-windows-lsl.dll> --json"),
            new SourceWorkspaceCommand(
                "run-lsl-loopback",
                "Write a local LSL loopback diagnostics bundle with JSON, CSV, Markdown, and PDF outputs.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- lsl loopback --lsl-dll <path-to-windows-lsl.dll> --count 16 --interval-ms 100 --out .\artifacts\lsl-loopback --json"),
            new SourceWorkspaceCommand(
                "run-broker-lsl-roundtrip",
                "Compare broker WebSocket latency samples against the broker-forwarded LSL stream.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- lsl broker-roundtrip --serial <serial> --lsl-dll <path-to-windows-lsl.dll> --count 8 --interval-ms 250 --out .\artifacts\lsl-broker --json"),
            new SourceWorkspaceCommand(
                "verify-environment-depth",
                "Install, launch, and validate OpenXR environment-depth acquisition diagnostics.",
                companionPath,
                @"dotnet run --project .\src\RustyXr.Companion.Cli -- catalog verify --path ..\Rusty-XR\examples\quest-composite-layer-apk\catalog\rusty-xr-quest-composite-layer.catalog.json --app rusty-xr-quest-composite-layer --serial <serial> --stop-catalog-apps --install --launch --device-profile xr-composite-smoke-test --runtime-profile environment-depth-diagnostics --settle-ms 9000 --logcat-lines 1400 --out .\artifacts\verify")
        };

        return new SourceWorkspaceStatus(
            root,
            rustyXrPath,
            companionPath,
            File.Exists(Path.Combine(rustyXrPath, "Cargo.toml")),
            File.Exists(Path.Combine(companionPath, "RustyXr.Companion.slnx")),
            companionCliProject,
            minimalBuildScript,
            minimalCatalog,
            minimalApk,
            File.Exists(minimalApk),
            compositeBuildScript,
            compositeCatalog,
            compositeApk,
            File.Exists(compositeApk),
            brokerBuildScript,
            brokerCatalog,
            brokerApk,
            File.Exists(brokerApk),
            new[]
            {
                "Keep both public repos as siblings under one workspace folder.",
                "Use the companion-managed tooling cache for adb, hzdb, and scrcpy.",
                "Install Rust and Android build tooling only on machines that build APKs from source.",
                "Build APK bytes under Rusty XR ignored build folders, then verify them through catalog commands.",
                "Keep diagnostics, screenshots, media frames, APKs, signing material, and local caches out of git."
            },
            commands);
    }

    public static string ToMarkdown(SourceWorkspaceStatus status)
    {
        var lines = new List<string>
        {
            "# Rusty XR Source Workspace",
            string.Empty,
            $"Workspace root: `{status.WorkspaceRoot}`",
            $"Rusty XR repo: `{status.RustyXrRepoPath}` ({FoundLabel(status.RustyXrRepoPresent)})",
            $"Companion repo: `{status.CompanionRepoPath}` ({FoundLabel(status.CompanionRepoPresent)})",
            string.Empty,
            "## Recommended Layout",
            string.Empty,
            "```text",
            @"<workspace>\Rusty-XR",
            @"<workspace>\Rusty-XR-Companion-Apps",
            "```",
            string.Empty,
            "## What The Companion Manages",
            string.Empty,
            "- Android platform-tools / adb",
            "- Meta hzdb",
            "- scrcpy for display casting",
            "- catalog APK downloads and verification bundles",
            string.Empty,
            "## Source APK Build Prerequisites",
            string.Empty,
            "- .NET 10 SDK for companion source builds",
            "- Rust with the aarch64-linux-android target for Rusty XR APK examples",
            "- Android SDK, NDK, build tools, and JDK layout accepted by the Rusty XR example build scripts",
            "- Quest-compatible OpenXR loader only for the immersive composite-layer example",
            "- Optional compliant Android liblsl.so only for LSL-capable broker APK builds",
            string.Empty,
            "## Agent Steps",
            string.Empty
        };

        lines.AddRange(status.AgentSteps.Select(static step => $"- {step}"));
        lines.AddRange(new[] { string.Empty, "## Commands", string.Empty });
        foreach (var command in status.Commands)
        {
            lines.Add($"### {command.Id}");
            lines.Add(command.Description);
            lines.Add(string.Empty);
            lines.Add("```powershell");
            lines.Add($"cd \"{command.WorkingDirectory}\"");
            lines.Add(command.Command);
            lines.Add("```");
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ResolveWorkspaceRoot(string? workspaceRoot, string currentDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return Path.GetFullPath(workspaceRoot);
        }

        var current = Path.GetFullPath(currentDirectory);
        var companionAncestor = FindAncestorNamed(current, CompanionRepoName);
        if (companionAncestor is not null)
        {
            return Directory.GetParent(companionAncestor)?.FullName ?? current;
        }

        var rustyXrAncestor = FindAncestorNamed(current, RustyXrRepoName);
        if (rustyXrAncestor is not null)
        {
            return Directory.GetParent(rustyXrAncestor)?.FullName ?? current;
        }

        return current;
    }

    private static string? FindAncestorNamed(string start, string name)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (string.Equals(directory.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string FoundLabel(bool found) => found ? "found" : "missing";
}

public sealed record SourceWorkspaceStatus(
    string WorkspaceRoot,
    string RustyXrRepoPath,
    string CompanionRepoPath,
    bool RustyXrRepoPresent,
    bool CompanionRepoPresent,
    string CompanionCliProjectPath,
    string MinimalBuildScriptPath,
    string MinimalCatalogPath,
    string MinimalApkPath,
    bool MinimalApkPresent,
    string CompositeBuildScriptPath,
    string CompositeCatalogPath,
    string CompositeApkPath,
    bool CompositeApkPresent,
    string BrokerBuildScriptPath,
    string BrokerCatalogPath,
    string BrokerApkPath,
    bool BrokerApkPresent,
    IReadOnlyList<string> AgentSteps,
    IReadOnlyList<SourceWorkspaceCommand> Commands);

public sealed record SourceWorkspaceCommand(
    string Id,
    string Description,
    string WorkingDirectory,
    string Command);
