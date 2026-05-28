using System.Buffers.Binary;
using RustyXr.Companion.Windows;

namespace RustyXr.Companion.Windows.Tests;

public sealed class PolarH10WindowsCaptureServiceTests
{
    [Fact]
    public void ParsesCompactOrDelimitedBluetoothAddress()
    {
        var compact = PolarH10WindowsCaptureService.ParseBluetoothAddress("A1B2C3D4E5F6");
        var delimited = PolarH10WindowsCaptureService.ParseBluetoothAddress("A1:B2:C3:D4:E5:F6");

        Assert.Equal(compact, delimited);
        Assert.Equal("A1:B2:C3:D4:E5:F6", PolarH10WindowsCaptureService.FormatAddress(compact));
    }

    [Fact]
    public void DecodesUncompressedAccFrameSummary()
    {
        var payload = new byte[10 + 12];
        payload[0] = 0x02;
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(1, 8), 123_456_789UL);
        payload[9] = 0x00;
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(10, 2), 10);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(12, 2), -20);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(14, 2), 30);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(16, 2), 20);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(18, 2), -40);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(20, 2), 60);

        var frame = PolarH10WindowsCaptureService.DecodeAccFrame(
            payload,
            sequence: 7,
            unixNs: 111,
            ticks: 222,
            deviceAddress: "A1:B2:C3:D4:E5:F6",
            deviceName: "Polar H10");

        Assert.NotNull(frame);
        Assert.Equal(PolarH10WindowsCaptureService.AccFrameSchema, frame.Schema);
        Assert.Equal(7, frame.Sequence);
        Assert.Equal(123_456_789UL, frame.SensorTimestampNs);
        Assert.Equal(2, frame.SampleCount);
        Assert.Equal(15.0, frame.MeanXMilliG);
        Assert.Equal(-30.0, frame.MeanYMilliG);
        Assert.Equal(45.0, frame.MeanZMilliG);
        Assert.Equal(2, frame.SamplesMilliG.Count);
    }

    [Fact]
    public void DecodesUncompressedEcgFrameSummary()
    {
        var payload = new byte[10 + 6];
        payload[0] = 0x00;
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(1, 8), 987_654_321UL);
        payload[9] = 0x00;
        payload[10] = 0x01;
        payload[11] = 0x00;
        payload[12] = 0x00;
        payload[13] = 0xff;
        payload[14] = 0xff;
        payload[15] = 0xff;

        var frame = PolarH10WindowsCaptureService.DecodeEcgFrame(
            payload,
            sequence: 3,
            unixNs: 111,
            ticks: 222,
            deviceAddress: "A1:B2:C3:D4:E5:F6",
            deviceName: "Polar H10");

        Assert.NotNull(frame);
        Assert.Equal(PolarH10WindowsCaptureService.EcgFrameSchema, frame.Schema);
        Assert.Equal(3, frame.Sequence);
        Assert.Equal(987_654_321UL, frame.SensorTimestampNs);
        Assert.Equal(2, frame.SampleCount);
        Assert.Equal(1, frame.FirstMicrovolts);
        Assert.Equal(-1, frame.MinMicrovolts);
        Assert.Equal(1, frame.MaxMicrovolts);
    }

    [Fact]
    public void DecodesHeartRateAndRrIntervals()
    {
        var payload = new byte[4];
        payload[0] = 0x10;
        payload[1] = 64;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2, 2), 1024);

        var record = PolarH10WindowsCaptureService.DecodeHeartRate(payload);

        Assert.NotNull(record);
        Assert.Equal(PolarH10WindowsCaptureService.HeartRateSchema, record.Schema);
        Assert.Equal(64, record.HeartRateBpm);
        Assert.Single(record.RrIntervalsMs);
        Assert.Equal(1000.0, record.RrIntervalsMs[0]);
    }
}
