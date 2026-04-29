using RustyXr.Companion.Core;
using RustyXr.Companion.Diagnostics;

namespace RustyXr.Companion.Diagnostics.Tests;

public sealed class DiagnosticsReportTests
{
    [Fact]
    public void MarkdownReportContainsToolAndDeviceSections()
    {
        var report = new DiagnosticsReport(
            DateTimeOffset.Parse("2026-04-29T12:00:00+00:00"),
            "machine",
            "user",
            "Windows",
            "10.0.0",
            new[] { new ToolStatus(ToolKind.Adb, "ADB", true, "adb.exe", "1.0", "Available.") },
            new[] { new QuestDevice("ABC", "device", "Quest", "vr") },
            Array.Empty<QuestSnapshot>(),
            Array.Empty<string>());

        var markdown = DiagnosticsBundleWriter.ToMarkdown(report);

        Assert.Contains("## Tools", markdown);
        Assert.Contains("## Devices", markdown);
        Assert.Contains("Quest", markdown);
    }
}
