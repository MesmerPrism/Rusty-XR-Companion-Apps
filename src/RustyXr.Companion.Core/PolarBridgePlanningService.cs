using System.Text;
using System.Text.Json;

namespace RustyXr.Companion.Core;

public sealed class PolarBridgePlanningService
{
    public const string SchemaVersion = "rusty.xr.companion.polar-bridge-plan.v1";

    public PolarBridgePlan Build(PolarBridgePlanOptions options)
    {
        var normalized = options.Normalize();
        var profiles = CreateProfiles();
        var selectedProfiles = string.IsNullOrWhiteSpace(normalized.ProfileId)
            ? profiles
            : profiles
                .Where(profile => string.Equals(profile.Id, normalized.ProfileId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        var notes = new List<string>
        {
            "Plan generation is device-free. Running a selected profile still requires explicit live device, broker, and transport setup.",
            "Treat Polar PMD raw streams as single-owner unless a sensor-specific live test proves otherwise."
        };

        if (selectedProfiles.Count == 0)
        {
            notes.Add($"Profile '{normalized.ProfileId}' was not found.");
        }

        return new PolarBridgePlan(
            SchemaVersion,
            DateTimeOffset.UtcNow,
            normalized.ProfileId,
            PlanGenerationRequiresLiveHardware: false,
            selectedProfiles,
            CreateTransportOptions(),
            CreateQualityGates(),
            notes);
    }

    public static IReadOnlyList<PolarBridgeProfile> CreateProfiles() =>
        new[]
        {
            new PolarBridgeProfile(
                "pc-owned-pmd",
                "PC-owned PMD bridge",
                "Use the Windows Companion to own Polar raw PMD streams, then forward decoded or framed data to the Quest broker for headset-side consumers.",
                PolarPmdOwner: "pc-companion",
                AllowsRawPmdDualReceiver: false,
                CapturedSignals: new[]
                {
                    "standard-hr-rr",
                    "polar-pmd-acc",
                    "polar-pmd-ecg-when-enabled"
                },
                RequiredLiveResources: new[]
                {
                    "Polar H10 online and paired to Windows",
                    "Quest broker reachable from the PC",
                    "one or more forwarding transports: lsl, osc, zeromq"
                },
                EndpointRoles: new[]
                {
                    new PolarBridgeEndpointRole(
                        "windows-companion",
                        "BLE acquisition owner",
                        "Stamp notifications as close to Windows receipt as possible and forward records without rewriting sensor timestamps.",
                        Inputs: new[] { "Polar H10 HRS notifications", "Polar H10 PMD notifications" },
                        Outputs: new[] { BrokerBioDiagnosticDefaults.PolarHeartRateStream, BrokerBioDiagnosticDefaults.PolarAccStream, BrokerBioDiagnosticDefaults.PolarEcgStream }),
                    new PolarBridgeEndpointRole(
                        "external-pipeline",
                        "Optional transform or analysis process",
                        "Keep original capture timestamps and add processing timestamps before forwarding.",
                        Inputs: new[] { "Companion-forwarded Polar frames" },
                        Outputs: new[] { "LSL outlet", "OSC messages", "ZeroMQ messages" }),
                    new PolarBridgeEndpointRole(
                        "quest-broker",
                        "Headset consumer and timestamp witness",
                        "Record broker receive time and publish stream events for plotting and headset-side consumers.",
                        Inputs: new[] { "LSL", "OSC", "ZeroMQ bridge output" },
                        Outputs: new[] { "broker stream events", "comparison capture records" })
                },
                TransportIds: new[] { "lsl", "osc", "zeromq" },
                CommandHints: new[]
                {
                    new PolarBridgeCommandHint(
                        "Generate this plan",
                        "rusty-xr-companion polar plan --profile pc-owned-pmd --out artifacts/polar-bridge --json",
                        RequiresLiveHardware: false),
                    new PolarBridgeCommandHint(
                        "Check LSL runtime",
                        "rusty-xr-companion lsl runtime --json",
                        RequiresLiveHardware: false),
                    new PolarBridgeCommandHint(
                        "Open broker control channel when the headset is available",
                        "rusty-xr-companion broker forward --serial <quest-serial> --json",
                        RequiresLiveHardware: true),
                    new PolarBridgeCommandHint(
                        "Run a broker clock probe when the headset is available",
                        "rusty-xr-companion lsl broker-roundtrip --serial <quest-serial> --out artifacts/lsl-clock --json",
                        RequiresLiveHardware: true)
                },
                Limitations: new[]
                {
                    "This profile cannot provide direct raw PMD comparison against a simultaneous Quest PMD receiver with a single Polar H10.",
                    "Compare LSL, OSC, and ZeroMQ forwarding latency using the same PC-owned source records."
                }),
            new PolarBridgeProfile(
                "quest-owned-pmd",
                "Quest-owned PMD relay",
                "Use the Quest broker to own Polar raw PMD streams and relay timestamped broker events back to the PC for plotting and optional pipeline ingestion.",
                PolarPmdOwner: "quest-broker",
                AllowsRawPmdDualReceiver: false,
                CapturedSignals: new[]
                {
                    "standard-hr-rr",
                    "polar-pmd-acc",
                    "polar-pmd-ecg-when-enabled"
                },
                RequiredLiveResources: new[]
                {
                    "Polar H10 online and paired to the headset",
                    "Quest broker with Polar BLE capture enabled",
                    "PC-to-Quest broker forward or LAN route for returning records"
                },
                EndpointRoles: new[]
                {
                    new PolarBridgeEndpointRole(
                        "quest-broker",
                        "BLE acquisition owner",
                        "Stamp direct headset receipt and publish broker stream events.",
                        Inputs: new[] { "Polar H10 HRS notifications", "Polar H10 PMD notifications" },
                        Outputs: new[] { BrokerBioDiagnosticDefaults.PolarHeartRateStream, BrokerBioDiagnosticDefaults.PolarAccStream, BrokerBioDiagnosticDefaults.PolarEcgStream }),
                    new PolarBridgeEndpointRole(
                        "windows-companion",
                        "Recorder and plotting witness",
                        "Subscribe to broker events and write comparison-ready records without commanding PMD.",
                        Inputs: new[] { "broker stream events" },
                        Outputs: new[] { "capture JSONL", "plotting manifest" })
                },
                TransportIds: new[] { "broker-websocket", "lsl-clock" },
                CommandHints: new[]
                {
                    new PolarBridgeCommandHint(
                        "Open broker control channel when the headset is available",
                        "rusty-xr-companion broker forward --serial <quest-serial> --json",
                        RequiresLiveHardware: true),
                    new PolarBridgeCommandHint(
                        "Run a broker clock probe when the headset is available",
                        "rusty-xr-companion lsl broker-roundtrip --serial <quest-serial> --out artifacts/lsl-clock --json",
                        RequiresLiveHardware: true)
                },
                Limitations: new[]
                {
                    "PC-side Goofi or analysis tools receive relayed records rather than owning the raw Polar PMD connection.",
                    "This profile is the clean direct-headset baseline for PMD, not a simultaneous PMD comparison."
                }),
            new PolarBridgeProfile(
                "hr-rr-dual-receiver",
                "HR/RR dual receiver comparison",
                "Use the standard Heart Rate Service on both PC and headset while leaving Polar PMD disabled, then compare notification timing and RR shape.",
                PolarPmdOwner: "disabled",
                AllowsRawPmdDualReceiver: false,
                CapturedSignals: new[]
                {
                    "standard-hr-rr"
                },
                RequiredLiveResources: new[]
                {
                    "Polar H10 online and advertising live heart-rate data",
                    "Windows Companion HRS subscription",
                    "Quest broker HRS subscription"
                },
                EndpointRoles: new[]
                {
                    new PolarBridgeEndpointRole(
                        "windows-companion",
                        "HRS receiver",
                        "Record HR/RR notifications and PC receipt timestamps.",
                        Inputs: new[] { "Polar H10 HRS notifications" },
                        Outputs: new[] { BrokerBioDiagnosticDefaults.PolarHeartRateStream }),
                    new PolarBridgeEndpointRole(
                        "quest-broker",
                        "HRS receiver",
                        "Record direct headset HR/RR notifications and broker receipt timestamps.",
                        Inputs: new[] { "Polar H10 HRS notifications" },
                        Outputs: new[] { BrokerBioDiagnosticDefaults.PolarHeartRateStream })
                },
                TransportIds: new[] { "broker-websocket", "lsl-clock" },
                CommandHints: new[]
                {
                    new PolarBridgeCommandHint(
                        "Simulate broker-side bio stream shape without devices",
                        "rusty-xr-companion broker bio-simulate --skip-ecg --skip-acc --count 16 --json",
                        RequiresLiveHardware: false)
                },
                Limitations: new[]
                {
                    "This profile validates dual BLE receiver behavior only for HR/RR, not raw PMD streams."
                }),
            new PolarBridgeProfile(
                "two-sensor-raw-compare",
                "Two-sensor raw PMD comparison",
                "Use two Polar-compatible sensors so PC and Quest can each own a raw PMD stream at the same time, then compare matched motion or stimulus windows.",
                PolarPmdOwner: "pc-and-quest-on-separate-sensors",
                AllowsRawPmdDualReceiver: true,
                CapturedSignals: new[]
                {
                    "standard-hr-rr",
                    "polar-pmd-acc",
                    "polar-pmd-ecg-when-enabled"
                },
                RequiredLiveResources: new[]
                {
                    "two Polar-compatible sensors",
                    "Windows Companion PMD subscription to sensor A",
                    "Quest broker PMD subscription to sensor B",
                    "shared clock probe for PC and Quest records"
                },
                EndpointRoles: new[]
                {
                    new PolarBridgeEndpointRole(
                        "windows-companion",
                        "Sensor A PMD owner",
                        "Record PC-side raw PMD frames for the mediated path.",
                        Inputs: new[] { "sensor A PMD notifications" },
                        Outputs: new[] { BrokerBioDiagnosticDefaults.PolarAccStream, BrokerBioDiagnosticDefaults.PolarEcgStream }),
                    new PolarBridgeEndpointRole(
                        "quest-broker",
                        "Sensor B PMD owner",
                        "Record headset-side raw PMD frames for the direct path.",
                        Inputs: new[] { "sensor B PMD notifications" },
                        Outputs: new[] { BrokerBioDiagnosticDefaults.PolarAccStream, BrokerBioDiagnosticDefaults.PolarEcgStream })
                },
                TransportIds: new[] { "lsl", "osc", "zeromq", "broker-websocket", "lsl-clock" },
                CommandHints: new[]
                {
                    new PolarBridgeCommandHint(
                        "Run a broker clock probe when the headset is available",
                        "rusty-xr-companion lsl broker-roundtrip --serial <quest-serial> --out artifacts/lsl-clock --json",
                        RequiresLiveHardware: true)
                },
                Limitations: new[]
                {
                    "Two physical sensors are not identical sources, so use synchronized stimuli or motion windows rather than sample-by-sample equality."
                })
        };

    public static IReadOnlyList<PolarBridgeTransport> CreateTransportOptions() =>
        new[]
        {
            new PolarBridgeTransport(
                "lsl",
                "Lab Streaming Layer",
                "PC to Quest and PC round-trip clock diagnostics",
                "float channels plus source and receipt timestamps",
                RequiredComponents: new[] { "liblsl runtime", "LSL outlet/inlet code" },
                Notes: new[]
                {
                    "Use for clock alignment and scientific tooling interoperability.",
                    "Preserve original Polar timestamps alongside LSL local clock timestamps."
                }),
            new PolarBridgeTransport(
                "osc",
                "Open Sound Control",
                "PC to Quest low-friction UDP messages",
                "addressed scalar/vector messages",
                RequiredComponents: new[] { "OSC sender", "broker OSC ingress" },
                Notes: new[]
                {
                    "Use for lightweight control and low-overhead signal forwarding.",
                    "Add sequence ids because UDP delivery is not guaranteed."
                }),
            new PolarBridgeTransport(
                "zeromq",
                "ZeroMQ-style bridge",
                "PC sidecar to Quest broker or companion-side bridge",
                "framed records with sequence ids, source timestamps, and payload metadata",
                RequiredComponents: new[] { "ZeroMQ bridge implementation", "broker-side receiver or sidecar" },
                Notes: new[]
                {
                    "Use the pure Rust implementation path for permissive-source prototypes where feature coverage is sufficient.",
                    "Keep native libzmq-based distributions in a separate sidecar with notices and source-disclosure handling."
                }),
            new PolarBridgeTransport(
                "broker-websocket",
                "Broker WebSocket events",
                "Quest broker to PC recorder",
                "broker stream events",
                RequiredComponents: new[] { "Quest broker", "Companion broker client" },
                Notes: new[]
                {
                    "Use as the common witness path for direct-headset captures."
                }),
            new PolarBridgeTransport(
                "lsl-clock",
                "LSL broker round-trip clock probe",
                "PC to Quest to PC timing diagnostics",
                "sequence, send time, broker receive time, return time",
                RequiredComponents: new[] { "LSL runtime", "Quest broker clock endpoint" },
                Notes: new[]
                {
                    "Run before and after captures so plots can annotate clock stability."
                })
        };

    public static IReadOnlyList<PolarBridgeQualityGate> CreateQualityGates() =>
        new[]
        {
            new PolarBridgeQualityGate(
                "clock-probe-present",
                "A PC to Quest to PC clock probe was captured for the same run window.",
                "At least one successful round-trip bundle exists before or during the capture.",
                "lsl broker-roundtrip JSON bundle"),
            new PolarBridgeQualityGate(
                "sequence-continuity",
                "Forwarded frames retain monotonic sequence ids per transport.",
                "No duplicate sequence ids and gaps are reported explicitly.",
                "capture JSONL and plotting manifest"),
            new PolarBridgeQualityGate(
                "timestamp-provenance",
                "Each record keeps source, PC receipt, bridge send, broker receipt, and plot ingestion timestamps when available.",
                "Every plotted point can name which timestamps are observed and which are derived.",
                "plotting manifest"),
            new PolarBridgeQualityGate(
                "pmd-single-owner-declared",
                "Every capture declares which endpoint owns Polar PMD.",
                "A single-H10 raw PMD run never labels PC and Quest as simultaneous PMD owners.",
                "polar bridge plan profile id")
        };
}

public static class PolarBridgePlanWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Write(PolarBridgePlan plan, string outputRoot)
    {
        var folder = Path.Combine(outputRoot, $"polar-bridge-plan-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "polar-bridge-plan.json"), JsonSerializer.Serialize(plan, JsonOptions));
        File.WriteAllText(Path.Combine(folder, "polar-bridge-plan.md"), ToMarkdown(plan));
        return folder;
    }

    public static string ToMarkdown(PolarBridgePlan plan)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Polar Bridge Plan");
        builder.AppendLine();
        builder.AppendLine($"Schema: `{plan.SchemaVersion}`");
        builder.AppendLine($"Generated: `{plan.GeneratedAt:O}`");
        builder.AppendLine($"Plan generation requires live hardware: `{plan.PlanGenerationRequiresLiveHardware}`");
        if (!string.IsNullOrWhiteSpace(plan.SelectedProfileId))
        {
            builder.AppendLine($"Selected profile: `{plan.SelectedProfileId}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Profiles");
        foreach (var profile in plan.Profiles)
        {
            builder.AppendLine();
            builder.AppendLine($"### {profile.Id}");
            builder.AppendLine(profile.Purpose);
            builder.AppendLine();
            builder.AppendLine($"- PMD owner: `{profile.PolarPmdOwner}`");
            builder.AppendLine($"- raw PMD dual receiver allowed: `{profile.AllowsRawPmdDualReceiver}`");
            builder.AppendLine($"- transports: `{string.Join("`, `", profile.TransportIds)}`");
            AppendList(builder, "Signals", profile.CapturedSignals);
            AppendList(builder, "Required live resources", profile.RequiredLiveResources);
            AppendList(builder, "Limitations", profile.Limitations);
        }

        builder.AppendLine();
        builder.AppendLine("## Quality Gates");
        foreach (var gate in plan.QualityGates)
        {
            builder.AppendLine($"- `{gate.Id}`: {gate.PassCondition}");
        }

        if (plan.Notes.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Notes");
            AppendList(builder, null, plan.Notes);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendList(StringBuilder builder, string? title, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.AppendLine();
            builder.AppendLine($"{title}:");
        }

        foreach (var value in values)
        {
            builder.AppendLine($"- {value}");
        }
    }
}

public sealed record PolarBridgePlanOptions(string? ProfileId = null)
{
    public PolarBridgePlanOptions Normalize() =>
        this with
        {
            ProfileId = string.IsNullOrWhiteSpace(ProfileId) ? null : ProfileId.Trim()
        };
}

public sealed record PolarBridgePlan(
    string SchemaVersion,
    DateTimeOffset GeneratedAt,
    string? SelectedProfileId,
    bool PlanGenerationRequiresLiveHardware,
    IReadOnlyList<PolarBridgeProfile> Profiles,
    IReadOnlyList<PolarBridgeTransport> TransportOptions,
    IReadOnlyList<PolarBridgeQualityGate> QualityGates,
    IReadOnlyList<string> Notes);

public sealed record PolarBridgeProfile(
    string Id,
    string Label,
    string Purpose,
    string PolarPmdOwner,
    bool AllowsRawPmdDualReceiver,
    IReadOnlyList<string> CapturedSignals,
    IReadOnlyList<string> RequiredLiveResources,
    IReadOnlyList<PolarBridgeEndpointRole> EndpointRoles,
    IReadOnlyList<string> TransportIds,
    IReadOnlyList<PolarBridgeCommandHint> CommandHints,
    IReadOnlyList<string> Limitations);

public sealed record PolarBridgeEndpointRole(
    string Id,
    string Role,
    string TimingResponsibility,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs);

public sealed record PolarBridgeTransport(
    string Id,
    string Label,
    string Direction,
    string PayloadShape,
    IReadOnlyList<string> RequiredComponents,
    IReadOnlyList<string> Notes);

public sealed record PolarBridgeQualityGate(
    string Id,
    string Description,
    string PassCondition,
    string Evidence);

public sealed record PolarBridgeCommandHint(
    string Label,
    string Command,
    bool RequiresLiveHardware);
