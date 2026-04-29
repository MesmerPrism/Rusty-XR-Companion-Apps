using System.Diagnostics;

namespace RustyXr.Companion.Core;

public sealed class ScrcpyService
{
    private readonly ToolLocator _toolLocator;

    public ScrcpyService(ToolLocator? toolLocator = null)
    {
        _toolLocator = toolLocator ?? new ToolLocator();
    }

    public StreamSession Start(StreamLaunchRequest request)
    {
        var scrcpy = _toolLocator.FindScrcpy();
        if (string.IsNullOrWhiteSpace(scrcpy))
        {
            throw new InvalidOperationException("scrcpy.exe was not found. Install scrcpy or add it to PATH.");
        }

        var args = new List<string>
        {
            "--serial",
            request.Serial,
            "--window-title",
            $"Rusty XR Cast {request.Serial}"
        };

        if (request.MaxSize is { } maxSize)
        {
            args.Add("--max-size");
            args.Add(maxSize.ToString());
        }

        if (request.BitRateMbps is { } bitrate)
        {
            args.Add("--video-bit-rate");
            args.Add($"{bitrate}M");
        }

        if (request.StayAwake)
        {
            args.Add("--stay-awake");
        }

        var arguments = string.Join(" ", args.Select(QuoteIfNeeded));
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = scrcpy,
            Arguments = arguments,
            UseShellExecute = false
        });

        if (process is null)
        {
            throw new InvalidOperationException("scrcpy could not be started.");
        }

        return new StreamSession(scrcpy, arguments, DateTimeOffset.Now, process.Id);
    }

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
