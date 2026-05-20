using System.Text.Json;
using RustyXr.Companion.Core;

namespace RustyXr.Companion.Core.Tests;

public sealed class PolarBridgePlanningServiceTests
{
    [Fact]
    public void PlanIncludesDeviceFreeProfiles()
    {
        var plan = new PolarBridgePlanningService().Build(new PolarBridgePlanOptions());

        Assert.Equal(PolarBridgePlanningService.SchemaVersion, plan.SchemaVersion);
        Assert.False(plan.PlanGenerationRequiresLiveHardware);
        Assert.Contains(plan.Profiles, static profile => profile.Id == "pc-owned-pmd");
        Assert.Contains(plan.Profiles, static profile => profile.Id == "quest-owned-pmd");
        Assert.Contains(plan.Profiles, static profile => profile.Id == "hr-rr-dual-receiver");
        Assert.Contains(plan.Profiles, static profile => profile.Id == "two-sensor-raw-compare");
        Assert.Contains(plan.QualityGates, static gate => gate.Id == "clock-probe-present");
    }

    [Fact]
    public void PcOwnedPmdProfileDeclaresSingleOwnerAndBridgeTransports()
    {
        var plan = new PolarBridgePlanningService()
            .Build(new PolarBridgePlanOptions("pc-owned-pmd"));
        var profile = Assert.Single(plan.Profiles);

        Assert.Equal("pc-companion", profile.PolarPmdOwner);
        Assert.False(profile.AllowsRawPmdDualReceiver);
        Assert.Contains("lsl", profile.TransportIds);
        Assert.Contains("osc", profile.TransportIds);
        Assert.Contains("zeromq", profile.TransportIds);
        Assert.Contains(profile.EndpointRoles, role => role.Outputs.Contains(BrokerBioDiagnosticDefaults.PolarAccStream));
        Assert.Contains(profile.Limitations, limitation => limitation.Contains("single Polar H10", StringComparison.Ordinal));
    }

    [Fact]
    public void HrRrDualReceiverProfileKeepsPmdDisabled()
    {
        var plan = new PolarBridgePlanningService()
            .Build(new PolarBridgePlanOptions("hr-rr-dual-receiver"));
        var profile = Assert.Single(plan.Profiles);

        Assert.Equal("disabled", profile.PolarPmdOwner);
        Assert.Equal(new[] { "standard-hr-rr" }, profile.CapturedSignals);
        Assert.DoesNotContain("polar-pmd-acc", profile.CapturedSignals);
    }

    [Fact]
    public void UnknownProfileReturnsEmptyPlanWithNote()
    {
        var plan = new PolarBridgePlanningService()
            .Build(new PolarBridgePlanOptions("missing-profile"));

        Assert.Empty(plan.Profiles);
        Assert.Contains(plan.Notes, note => note.Contains("missing-profile", StringComparison.Ordinal));
    }

    [Fact]
    public void WriterCreatesJsonAndMarkdownBundle()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rusty-xr-polar-plan-{Guid.NewGuid():N}");
        var plan = new PolarBridgePlanningService()
            .Build(new PolarBridgePlanOptions("pc-owned-pmd"));

        try
        {
            var folder = PolarBridgePlanWriter.Write(plan, root);
            var jsonPath = Path.Combine(folder, "polar-bridge-plan.json");
            var markdownPath = Path.Combine(folder, "polar-bridge-plan.md");

            Assert.True(File.Exists(jsonPath));
            Assert.True(File.Exists(markdownPath));
            using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            Assert.Equal(
                PolarBridgePlanningService.SchemaVersion,
                document.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Contains("pc-owned-pmd", File.ReadAllText(markdownPath), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
