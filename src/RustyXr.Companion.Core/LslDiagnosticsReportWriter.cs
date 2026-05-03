using System.Globalization;
using System.Text;
using System.Text.Json;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace RustyXr.Companion.Core;

public static class LslDiagnosticsReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string WriteLocal(LslLocalRoundTripReport report, string outputRoot, bool includePdf = true)
    {
        var folder = CreateReportFolder(outputRoot, "lsl-local-roundtrip");
        File.WriteAllText(Path.Combine(folder, "lsl-local-roundtrip.json"), JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(Path.Combine(folder, "lsl-local-roundtrip.csv"), LocalCsv(report));
        File.WriteAllText(Path.Combine(folder, "README.md"), LocalMarkdown(report));
        if (includePdf)
        {
            LslDiagnosticsPdfRenderer.RenderLocal(report, Path.Combine(folder, "lsl-local-roundtrip.pdf"));
        }

        return folder;
    }

    public static string WriteBroker(LslBrokerRoundTripReport report, string outputRoot, bool includePdf = true)
    {
        var folder = CreateReportFolder(outputRoot, "lsl-broker-roundtrip");
        File.WriteAllText(Path.Combine(folder, "lsl-broker-roundtrip.json"), JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(Path.Combine(folder, "lsl-broker-roundtrip.csv"), BrokerCsv(report));
        File.WriteAllText(Path.Combine(folder, "README.md"), BrokerMarkdown(report));
        if (includePdf)
        {
            LslDiagnosticsPdfRenderer.RenderBroker(report, Path.Combine(folder, "lsl-broker-roundtrip.pdf"));
        }

        return folder;
    }

    private static string CreateReportFolder(string outputRoot, string prefix)
    {
        var folder = Path.Combine(outputRoot, $"{prefix}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string LocalCsv(LslLocalRoundTripReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("sequence,matched,host_send_unix_ns,host_receive_unix_ns,host_wall_ms,lsl_corrected_receive_delay_ms,time_correction_offset_ms,time_correction_uncertainty_ms");
        foreach (var sample in report.Samples)
        {
            builder.AppendLine(string.Join(
                ",",
                sample.Sequence.ToString(CultureInfo.InvariantCulture),
                sample.Matched ? "true" : "false",
                sample.HostSendUnixNs.ToString(CultureInfo.InvariantCulture),
                sample.HostReceiveUnixNs.ToString(CultureInfo.InvariantCulture),
                Format(sample.HostSendToReceiveWallMs),
                Format(sample.LslCorrectedSampleToReceiveMs),
                Format(sample.TimeCorrectionOffsetMs),
                Format(sample.TimeCorrectionUncertaintyMs)));
        }

        return builder.ToString();
    }

    private static string BrokerCsv(LslBrokerRoundTripReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("sequence,matched,host_send_unix_ns,host_receive_unix_ns,broker_receive_unix_ns,broker_publish_unix_ns,host_to_broker_receive_ms,broker_processing_ms,host_to_lsl_receive_wall_ms,lsl_corrected_receive_delay_ms,time_correction_offset_ms,time_correction_uncertainty_ms,websocket_messages");
        foreach (var sample in report.Samples)
        {
            builder.AppendLine(string.Join(
                ",",
                sample.Sequence.ToString(CultureInfo.InvariantCulture),
                sample.MatchedLslSample ? "true" : "false",
                sample.HostSendUnixNs.ToString(CultureInfo.InvariantCulture),
                sample.HostReceiveUnixNs.ToString(CultureInfo.InvariantCulture),
                sample.BrokerReceiveUnixNs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                sample.BrokerPublishUnixNs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                Format(sample.HostToBrokerReceiveMs),
                Format(sample.BrokerProcessingMs),
                Format(sample.HostSendToLslReceiveWallMs),
                Format(sample.LslCorrectedSampleToReceiveMs),
                Format(sample.TimeCorrectionOffsetMs),
                Format(sample.TimeCorrectionUncertaintyMs),
                sample.WebSocketMessages.ToString(CultureInfo.InvariantCulture)));
        }

        return builder.ToString();
    }

    private static string LocalMarkdown(LslLocalRoundTripReport report) =>
        $"""
        # LSL Local Round Trip

        - captured: {report.CapturedAt:O}
        - runtime: {(report.Runtime.Available ? "available" : "unavailable")} - {report.Runtime.Detail}
        - stream: {report.Options.StreamName} / {report.Options.StreamType}
        - matched samples: {report.Summary.MatchedSamples}/{report.Summary.SampleCount}
        - mean host loopback: {Format(report.Summary.MeanHostRoundTripMs)} ms
        - mean LSL receive delay: {Format(report.Summary.MeanLslReceiveDelayMs)} ms
        - mean LSL uncertainty: {Format(report.Summary.MeanTimeCorrectionUncertaintyMs)} ms
        """;

    private static string BrokerMarkdown(LslBrokerRoundTripReport report) =>
        $"""
        # LSL Broker Round Trip

        - captured: {report.CapturedAt:O}
        - runtime: {(report.Runtime.Available ? "available" : "unavailable")} - {report.Runtime.Detail}
        - broker: {report.Options.BrokerHost}:{report.Options.BrokerPort}
        - stream: {report.Options.StreamName} / {report.Options.StreamType}
        - matched LSL samples: {report.Summary.MatchedSamples}/{report.Summary.SampleCount}
        - mean host-to-LSL receive: {Format(report.Summary.MeanHostRoundTripMs)} ms
        - mean broker processing: {Format(report.Summary.MeanBrokerProcessingMs)} ms
        - mean LSL receive delay: {Format(report.Summary.MeanLslReceiveDelayMs)} ms
        - mean LSL uncertainty: {Format(report.Summary.MeanTimeCorrectionUncertaintyMs)} ms
        """;

    private static string Format(double? value) =>
        value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
}

public static class LslDiagnosticsPdfRenderer
{
    private const string TitleStyle = "ReportTitle";
    private const string SectionStyle = "ReportSection";
    private const string BodyStyle = "ReportBody";
    private const string MetaStyle = "ReportMeta";
    private const string HeaderStyle = "ReportHeader";
    private const string DenseStyle = "ReportDense";

    public static void RenderLocal(LslLocalRoundTripReport report, string outputPdfPath)
    {
        ArgumentNullException.ThrowIfNull(report);
        var document = CreateDocument("Rusty XR LSL Local Round Trip");
        var section = document.AddSection();
        AddTitle(section, "LSL Local Round Trip", report.CapturedAt);
        AddSummary(section, report.Runtime, report.Summary, report.Options.StreamName, report.Options.StreamType);
        AddKeyValues(
            section,
            "Loopback Configuration",
            [
                ("count", report.Options.Count.ToString(CultureInfo.InvariantCulture)),
                ("interval", $"{report.Options.IntervalMilliseconds} ms"),
                ("timeout", $"{report.Options.TimeoutMilliseconds} ms"),
                ("warmup", $"{report.Options.WarmupMilliseconds} ms"),
                ("source id", report.Options.SourceId)
            ]);
        AddLocalSamples(section, report.Samples);
        Save(document, outputPdfPath);
    }

    public static void RenderBroker(LslBrokerRoundTripReport report, string outputPdfPath)
    {
        ArgumentNullException.ThrowIfNull(report);
        var document = CreateDocument("Rusty XR LSL Broker Round Trip");
        var section = document.AddSection();
        AddTitle(section, "LSL Broker Round Trip", report.CapturedAt);
        AddSummary(section, report.Runtime, report.Summary, report.Options.StreamName, report.Options.StreamType);
        AddKeyValues(
            section,
            "Broker Configuration",
            [
                ("broker", $"{report.Options.BrokerHost}:{report.Options.BrokerPort}"),
                ("path", report.Options.Path),
                ("count", report.Options.Count.ToString(CultureInfo.InvariantCulture)),
                ("interval", $"{report.Options.IntervalMilliseconds} ms"),
                ("timeout", $"{report.Options.TimeoutMilliseconds} ms"),
                ("warmup", $"{report.Options.WarmupMilliseconds} ms")
            ]);
        AddBrokerSamples(section, report.Samples);
        Save(document, outputPdfPath);
    }

    private static Document CreateDocument(string title)
    {
        CompanionPdfReportBootstrap.EnsureInitialized();
        var document = new Document();
        document.Info.Title = title;
        document.Info.Author = "Rusty XR Companion";
        DefineStyles(document);
        return document;
    }

    private static void DefineStyles(Document document)
    {
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = Unit.FromPoint(8.5);
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(3);

        var title = document.Styles.AddStyle(TitleStyle, StyleNames.Normal);
        title.Font.Size = Unit.FromPoint(18);
        title.Font.Bold = true;
        title.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);

        var section = document.Styles.AddStyle(SectionStyle, StyleNames.Normal);
        section.Font.Size = Unit.FromPoint(11);
        section.Font.Bold = true;
        section.ParagraphFormat.SpaceBefore = Unit.FromPoint(9);
        section.ParagraphFormat.SpaceAfter = Unit.FromPoint(3);

        var body = document.Styles.AddStyle(BodyStyle, StyleNames.Normal);
        body.Font.Size = Unit.FromPoint(8.4);

        var meta = document.Styles.AddStyle(MetaStyle, StyleNames.Normal);
        meta.Font.Size = Unit.FromPoint(7.3);
        meta.Font.Color = Colors.Gray;

        var header = document.Styles.AddStyle(HeaderStyle, StyleNames.Normal);
        header.Font.Size = Unit.FromPoint(7.3);
        header.Font.Bold = true;

        var dense = document.Styles.AddStyle(DenseStyle, StyleNames.Normal);
        dense.Font.Size = Unit.FromPoint(6.8);
    }

    private static void AddTitle(Section section, string title, DateTimeOffset capturedAt)
    {
        AddParagraph(section, title, TitleStyle);
        AddParagraph(section, $"Captured {capturedAt:yyyy-MM-dd HH:mm:ss 'UTC'}", MetaStyle);
    }

    private static void AddSummary(
        Section section,
        LslRuntimeState runtime,
        LslRoundTripSummary summary,
        string streamName,
        string streamType)
    {
        AddKeyValues(
            section,
            "Summary",
            [
                ("runtime", $"{(runtime.Available ? "available" : "unavailable")} - {runtime.Detail}"),
                ("stream", $"{streamName} / {streamType}"),
                ("matched", $"{summary.MatchedSamples}/{summary.SampleCount}"),
                ("mean host round trip", FormatMs(summary.MeanHostRoundTripMs)),
                ("min/max host round trip", $"{FormatMs(summary.MinHostRoundTripMs)} / {FormatMs(summary.MaxHostRoundTripMs)}"),
                ("mean LSL receive delay", FormatMs(summary.MeanLslReceiveDelayMs)),
                ("mean LSL uncertainty", FormatMs(summary.MeanTimeCorrectionUncertaintyMs)),
                ("mean broker processing", FormatMs(summary.MeanBrokerProcessingMs))
            ]);
    }

    private static void AddLocalSamples(Section section, IReadOnlyList<LslLocalRoundTripSample> samples)
    {
        AddParagraph(section, "Samples", SectionStyle);
        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(1.5));
        table.AddColumn(Unit.FromCentimeter(1.8));
        table.AddColumn(Unit.FromCentimeter(3.0));
        table.AddColumn(Unit.FromCentimeter(3.0));
        table.AddColumn(Unit.FromCentimeter(3.0));
        table.AddColumn(Unit.FromCentimeter(3.0));
        AddHeader(table, ["seq", "match", "wall ms", "lsl delay ms", "offset ms", "uncert ms"]);
        foreach (var sample in samples.Take(48))
        {
            var row = table.AddRow();
            AddCell(row.Cells[0], sample.Sequence.ToString(CultureInfo.InvariantCulture));
            AddCell(row.Cells[1], sample.Matched ? "yes" : "no");
            AddCell(row.Cells[2], Format(sample.HostSendToReceiveWallMs));
            AddCell(row.Cells[3], Format(sample.LslCorrectedSampleToReceiveMs));
            AddCell(row.Cells[4], Format(sample.TimeCorrectionOffsetMs));
            AddCell(row.Cells[5], Format(sample.TimeCorrectionUncertaintyMs));
        }
    }

    private static void AddBrokerSamples(Section section, IReadOnlyList<LslBrokerRoundTripSample> samples)
    {
        AddParagraph(section, "Samples", SectionStyle);
        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(1.4));
        table.AddColumn(Unit.FromCentimeter(1.8));
        table.AddColumn(Unit.FromCentimeter(3.0));
        table.AddColumn(Unit.FromCentimeter(3.0));
        table.AddColumn(Unit.FromCentimeter(3.0));
        table.AddColumn(Unit.FromCentimeter(3.0));
        AddHeader(table, ["seq", "match", "host->lsl", "host->broker", "broker proc", "uncert"]);
        foreach (var sample in samples.Take(48))
        {
            var row = table.AddRow();
            AddCell(row.Cells[0], sample.Sequence.ToString(CultureInfo.InvariantCulture));
            AddCell(row.Cells[1], sample.MatchedLslSample ? "yes" : "no");
            AddCell(row.Cells[2], Format(sample.HostSendToLslReceiveWallMs));
            AddCell(row.Cells[3], Format(sample.HostToBrokerReceiveMs));
            AddCell(row.Cells[4], Format(sample.BrokerProcessingMs));
            AddCell(row.Cells[5], Format(sample.TimeCorrectionUncertaintyMs));
        }
    }

    private static void AddKeyValues(Section section, string title, IReadOnlyList<(string Key, string Value)> rows)
    {
        AddParagraph(section, title, SectionStyle);
        var table = CreateTable(section);
        table.AddColumn(Unit.FromCentimeter(4.0));
        table.AddColumn(Unit.FromCentimeter(12.5));
        foreach (var (key, value) in rows)
        {
            var row = table.AddRow();
            AddCell(row.Cells[0], key, HeaderStyle);
            AddCell(row.Cells[1], value, BodyStyle);
        }
    }

    private static Table CreateTable(Section section)
    {
        var table = section.AddTable();
        table.Borders.Width = Unit.FromPoint(0.35);
        table.Borders.Color = Colors.LightGray;
        table.Rows.LeftIndent = Unit.Zero;
        return table;
    }

    private static void AddHeader(Table table, IReadOnlyList<string> labels)
    {
        var row = table.AddRow();
        row.Shading.Color = Colors.LightGray;
        foreach (var (label, index) in labels.Select((label, index) => (label, index)))
        {
            AddCell(row.Cells[index], label, HeaderStyle);
        }
    }

    private static void AddCell(Cell cell, string text, string style = DenseStyle)
    {
        cell.VerticalAlignment = VerticalAlignment.Center;
        AddParagraph(cell, string.IsNullOrWhiteSpace(text) ? " " : text, style);
    }

    private static void AddParagraph(DocumentObject target, string text, string style)
    {
        Paragraph paragraph = target switch
        {
            Section section => section.AddParagraph(),
            Cell cell => cell.AddParagraph(),
            _ => throw new ArgumentException("Unsupported paragraph target.", nameof(target))
        };
        paragraph.Style = style;
        paragraph.AddText(text);
    }

    private static void Save(Document document, string outputPdfPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPdfPath))!);
        var renderer = new PdfDocumentRenderer
        {
            Document = document
        };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(outputPdfPath);
    }

    private static string FormatMs(double? value) =>
        value.HasValue ? $"{Format(value)} ms" : "n/a";

    private static string Format(double? value) =>
        value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
}
