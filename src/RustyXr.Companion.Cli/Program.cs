using System.Text.Json;
using RustyXr.Companion.Core;
using RustyXr.Companion.Diagnostics;

var exitCode = await CliProgram.RunAsync(args).ConfigureAwait(false);
return exitCode;

internal static class CliProgram
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            WriteHelp();
            return 0;
        }

        try
        {
            var command = args[0].ToLowerInvariant();
            var options = ArgOptions.Parse(args.Skip(1));
            return command switch
            {
                "doctor" => await DoctorAsync(options).ConfigureAwait(false),
                "devices" => await DevicesAsync(options).ConfigureAwait(false),
                "connect" => await ConnectAsync(options).ConfigureAwait(false),
                "snapshot" => await SnapshotAsync(options).ConfigureAwait(false),
                "install" => await InstallAsync(options).ConfigureAwait(false),
                "launch" => await LaunchAsync(options).ConfigureAwait(false),
                "stop" => await StopAsync(options).ConfigureAwait(false),
                "profile" => await ProfileAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "cast" => Cast(options),
                "catalog" => await CatalogAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                _ => Fail($"Unknown command '{command}'. Run --help for commands.")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<int> DoctorAsync(ArgOptions options)
    {
        var analyzer = new WindowsEnvironmentAnalyzer();
        var report = await analyzer.AnalyzeAsync(options.Has("--snapshots")).ConfigureAwait(false);
        if (options.Has("--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        }
        else
        {
            Console.WriteLine(DiagnosticsBundleWriter.ToMarkdown(report));
        }

        if (options.TryGet("--out", out var outputRoot))
        {
            var folder = await new DiagnosticsBundleWriter().WriteAsync(report, outputRoot).ConfigureAwait(false);
            Console.Error.WriteLine($"Diagnostics bundle written to {folder}");
        }

        return 0;
    }

    private static async Task<int> DevicesAsync(ArgOptions options)
    {
        var devices = await new QuestAdbService().ListDevicesAsync().ConfigureAwait(false);
        WriteObject(devices, options.Has("--json"));
        return devices.Any(static device => device.IsOnline) ? 0 : 2;
    }

    private static async Task<int> ConnectAsync(ArgOptions options)
    {
        var endpointText = Required(options, "--endpoint");
        if (!QuestEndpoint.TryParse(endpointText, out var endpoint))
        {
            return Fail($"Invalid endpoint '{endpointText}'. Use host:port or host.");
        }

        var result = await new QuestAdbService().ConnectAsync(endpoint).ConfigureAwait(false);
        WriteCommandResult(result);
        return result.Succeeded ? 0 : result.ExitCode;
    }

    private static async Task<int> SnapshotAsync(ArgOptions options)
    {
        var snapshot = await new QuestAdbService().GetSnapshotAsync(Required(options, "--serial")).ConfigureAwait(false);
        WriteObject(snapshot, options.Has("--json"));
        return 0;
    }

    private static async Task<int> InstallAsync(ArgOptions options)
    {
        var result = await new QuestAdbService()
            .InstallAsync(Required(options, "--serial"), Required(options, "--apk"))
            .ConfigureAwait(false);
        WriteCommandResult(result);
        return result.Succeeded ? 0 : result.ExitCode;
    }

    private static async Task<int> LaunchAsync(ArgOptions options)
    {
        var result = await new QuestAdbService()
            .LaunchAsync(Required(options, "--serial"), Required(options, "--package"), options.ValueOrNull("--activity"))
            .ConfigureAwait(false);
        WriteCommandResult(result);
        return result.Succeeded ? 0 : result.ExitCode;
    }

    private static async Task<int> StopAsync(ArgOptions options)
    {
        var result = await new QuestAdbService()
            .StopAsync(Required(options, "--serial"), Required(options, "--package"))
            .ConfigureAwait(false);
        WriteCommandResult(result);
        return result.Succeeded ? 0 : result.ExitCode;
    }

    private static async Task<int> ProfileAsync(string[] args)
    {
        if (args.Length == 0 || args[0] != "apply")
        {
            return Fail("Use: profile apply --serial <serial> [--cpu <level>] [--gpu <level>] [--prop key=value]");
        }

        var options = ArgOptions.Parse(args.Skip(1));
        var properties = options.Values("--prop")
            .Select(static value => value.Split('=', 2))
            .Where(static parts => parts.Length == 2 && parts[0].Length > 0)
            .ToDictionary(static parts => parts[0], static parts => parts[1], StringComparer.Ordinal);

        int? cpu = options.TryGet("--cpu", out var cpuText) ? int.Parse(cpuText) : null;
        int? gpu = options.TryGet("--gpu", out var gpuText) ? int.Parse(gpuText) : null;

        var results = await new QuestAdbService()
            .ApplyDeviceProfileAsync(Required(options, "--serial"), cpu, gpu, properties)
            .ConfigureAwait(false);

        foreach (var result in results)
        {
            WriteCommandResult(result);
        }

        return results.All(static result => result.Succeeded) ? 0 : 1;
    }

    private static int Cast(ArgOptions options)
    {
        int? maxSize = options.TryGet("--max-size", out var maxSizeText) ? int.Parse(maxSizeText) : null;
        int? bitrate = options.TryGet("--bitrate-mbps", out var bitrateText) ? int.Parse(bitrateText) : null;
        var session = new ScrcpyService().Start(new StreamLaunchRequest(Required(options, "--serial"), maxSize, bitrate));
        Console.WriteLine($"Started scrcpy process {session.ProcessId}: {session.ToolPath} {session.Arguments}");
        return 0;
    }

    private static async Task<int> CatalogAsync(string[] args)
    {
        if (args.Length == 0 || args[0] != "list")
        {
            return Fail("Use: catalog list --path <catalog.json>");
        }

        var options = ArgOptions.Parse(args.Skip(1));
        var path = options.ValueOrNull("--path") ?? Path.Combine("samples", "quest-session-kit", "apk-catalog.example.json");
        var catalog = await new CatalogLoader().LoadAsync(path).ConfigureAwait(false);
        WriteObject(catalog, options.Has("--json"));
        return 0;
    }

    private static void WriteObject<T>(T value, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
            return;
        }

        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }

    private static void WriteCommandResult(CommandResult result)
    {
        Console.WriteLine($"> {result.FileName} {result.Arguments}");
        Console.WriteLine($"Exit: {result.ExitCode} Duration: {result.Duration}");
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            Console.WriteLine(result.StandardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            Console.Error.WriteLine(result.StandardError.TrimEnd());
        }
    }

    private static string Required(ArgOptions options, string name) =>
        options.TryGet(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{name} is required.");

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static void WriteHelp()
    {
        Console.WriteLine("""
        Rusty XR Companion CLI

        Commands:
          doctor [--snapshots] [--json] [--out <folder>]
          devices [--json]
          connect --endpoint <host[:port]>
          snapshot --serial <serial> [--json]
          install --serial <serial> --apk <path>
          launch --serial <serial> --package <package> [--activity <activity>]
          stop --serial <serial> --package <package>
          profile apply --serial <serial> [--cpu <level>] [--gpu <level>] [--prop key=value]
          cast --serial <serial> [--max-size <pixels>] [--bitrate-mbps <n>]
          catalog list [--path <catalog.json>] [--json]
        """);
    }

    private sealed class ArgOptions
    {
        private readonly Dictionary<string, List<string>> _values;

        private ArgOptions(Dictionary<string, List<string>> values)
        {
            _values = values;
        }

        public static ArgOptions Parse(IEnumerable<string> args)
        {
            var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string? current = null;
            foreach (var arg in args)
            {
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    current = arg;
                    if (!values.ContainsKey(current))
                    {
                        values[current] = new List<string>();
                    }
                    continue;
                }

                if (current is null)
                {
                    continue;
                }

                values[current].Add(arg);
            }

            return new ArgOptions(values);
        }

        public bool Has(string key) => _values.ContainsKey(key);
        public bool TryGet(string key, out string value)
        {
            value = string.Empty;
            if (!_values.TryGetValue(key, out var values) || values.Count == 0)
            {
                return false;
            }

            value = values[^1];
            return true;
        }

        public string? ValueOrNull(string key) => TryGet(key, out var value) ? value : null;
        public IReadOnlyList<string> Values(string key) => _values.TryGetValue(key, out var values) ? values : Array.Empty<string>();
    }
}
