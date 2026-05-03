using RustyXr.Companion.Core;

namespace RustyXr.Companion.Core.Tests;

public sealed class BrokerBioSimulationServiceTests
{
    [Fact]
    public void HeartRateMeasurementPayloadMatchesBleCharacteristicShape()
    {
        var payload = SyntheticPolarPayloads.BuildHeartRateMeasurement(72, [833.333f]);

        Assert.Equal(0x16, payload[0]);
        Assert.Equal(72, payload[1]);
        var rrRaw = payload[2] | (payload[3] << 8);
        Assert.Equal(853, rrRaw);
    }

    [Fact]
    public void EcgPmdFrameUsesPolarHeaderAndSigned24BitSamples()
    {
        var payload = SyntheticPolarPayloads.BuildEcgPmdFrame(0x0102030405060708UL, [1, -1]);

        Assert.Equal(0x00, payload[0]);
        Assert.Equal(new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 }, payload[1..9]);
        Assert.Equal(0x00, payload[9]);
        Assert.Equal(new byte[] { 0x01, 0x00, 0x00 }, payload[10..13]);
        Assert.Equal(new byte[] { 0xff, 0xff, 0xff }, payload[13..16]);
    }

    [Fact]
    public void AccPmdFrameUsesPolarHeaderAndLittleEndianAxes()
    {
        var payload = SyntheticPolarPayloads.BuildAccPmdFrame(
            2UL,
            [new PolarAccSample(1000, -1000, 0)]);

        Assert.Equal(0x02, payload[0]);
        Assert.Equal(0x01, payload[9]);
        Assert.Equal(new byte[] { 0xe8, 0x03, 0x18, 0xfc, 0x00, 0x00 }, payload[10..16]);
    }

    [Fact]
    public void BuildCycleCreatesPolarCompatibleStreams()
    {
        var cycle = BrokerBioSimulationService.BuildCycle(1, new BrokerBioSimulationOptions());

        Assert.Contains(cycle, static sample => sample.Stream == BrokerBioDiagnosticDefaults.PolarHeartRateStream);
        Assert.Contains(cycle, static sample => sample.Stream == BrokerBioDiagnosticDefaults.PolarEcgStream);
        Assert.Contains(cycle, static sample => sample.Stream == BrokerBioDiagnosticDefaults.PolarAccStream);
        Assert.All(cycle, static sample => Assert.True(sample.PayloadBytes.Length > 0));
    }

    [Fact]
    public void BuildCycleDescribesStandardHeartRateAndPolarPmdGattLanes()
    {
        var cycle = BrokerBioSimulationService.BuildCycle(1, new BrokerBioSimulationOptions());

        var heartRate = Assert.Single(cycle, static sample => sample.Kind == "hr_rr");
        var heartRateGatt = heartRate.Payload["gatt"]!.AsObject();
        Assert.Equal(BrokerBioDiagnosticDefaults.StandardHeartRateGattProfile, heartRate.Payload["ble_profile"]!.GetValue<string>());
        Assert.Equal(BrokerBioDiagnosticDefaults.HeartRateServiceUuid, heartRateGatt["service_uuid"]!.GetValue<string>());
        Assert.Equal(BrokerBioDiagnosticDefaults.HeartRateMeasurementUuid, heartRateGatt["characteristic_uuid"]!.GetValue<string>());

        foreach (var pmd in cycle.Where(static sample => sample.Kind is "ecg" or "acc"))
        {
            var gatt = pmd.Payload["gatt"]!.AsObject();
            var pmdInfo = pmd.Payload["pmd"]!.AsObject();
            Assert.Equal(BrokerBioDiagnosticDefaults.PolarPmdGattProfile, pmd.Payload["ble_profile"]!.GetValue<string>());
            Assert.Equal(BrokerBioDiagnosticDefaults.PolarPmdServiceUuid, gatt["service_uuid"]!.GetValue<string>());
            Assert.Equal(BrokerBioDiagnosticDefaults.PolarPmdDataUuid, gatt["characteristic_uuid"]!.GetValue<string>());
            Assert.Equal(BrokerBioDiagnosticDefaults.PolarPmdControlPointUuid, pmdInfo["control_point_uuid"]!.GetValue<string>());
        }
    }
}
