using System.Security.Cryptography;
using RustyXr.Companion.Core;

namespace RustyXr.Companion.Core.Tests;

public sealed class RawLumaArtifactInspectionServiceTests
{
    [Fact]
    public async Task InspectReportsFrameStatsHashAndContactSheet()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"rusty-xr-raw-luma-inspect-{Guid.NewGuid():N}");
        var payloadPath = Path.Combine(tempRoot, "luma.raw");
        var contactSheetPath = Path.Combine(tempRoot, "luma.pgm");
        var payload = new byte[]
        {
            0, 10, 20, 30,
            40, 50, 60, 70
        };
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllBytesAsync(payloadPath, payload);

        try
        {
            var report = await new RawLumaArtifactInspectionService()
                .InspectAsync(new RawLumaArtifactInspectionOptions(
                    payloadPath,
                    Width: 2,
                    Height: 2,
                    ContactSheetPath: contactSheetPath));

            Assert.Equal(Path.GetFullPath(payloadPath), report.PayloadPath);
            Assert.Equal("raw_luma8", report.Codec);
            Assert.Equal(2, report.Width);
            Assert.Equal(2, report.Height);
            Assert.Equal(4, report.FrameSizeBytes);
            Assert.Equal(payload.Length, report.ByteCount);
            Assert.Equal(2, report.CompleteFrameCount);
            Assert.Equal(0, report.TrailingBytes);
            Assert.True(report.HasCompleteFrames);
            Assert.True(report.IsFrameAligned);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), report.Sha256);
            Assert.Equal(2, report.Frames.Count);
            Assert.Equal(60, report.Frames[0].PayloadChecksum);
            Assert.Equal(0, report.Frames[0].FirstByte);
            Assert.Equal(30, report.Frames[0].LastByte);
            Assert.Equal(0, report.Frames[0].MinByte);
            Assert.Equal(30, report.Frames[0].MaxByte);
            Assert.Equal(15.0, report.Frames[0].MeanByte);
            Assert.Equal(220, report.Frames[1].PayloadChecksum);
            Assert.Equal(55.0, report.Frames[1].MeanByte);
            Assert.NotNull(report.ContactSheet);
            Assert.Equal(Path.GetFullPath(contactSheetPath), report.ContactSheet.Path);
            Assert.Equal(2, report.ContactSheet.Width);
            Assert.Equal(4, report.ContactSheet.Height);
            Assert.Equal(2, report.ContactSheet.FrameCount);

            var pgm = await File.ReadAllBytesAsync(contactSheetPath);
            var expectedHeader = System.Text.Encoding.ASCII.GetBytes("P5\n2 4\n255\n");
            Assert.True(pgm.AsSpan(0, expectedHeader.Length).SequenceEqual(expectedHeader));
            Assert.True(pgm.AsSpan(expectedHeader.Length).SequenceEqual(payload));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task InspectReportsTrailingBytesWithoutCreatingEmptyContactSheet()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"rusty-xr-raw-luma-trailing-{Guid.NewGuid():N}");
        var payloadPath = Path.Combine(tempRoot, "luma.raw");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllBytesAsync(payloadPath, new byte[] { 1, 2, 3 });

        try
        {
            var report = await new RawLumaArtifactInspectionService()
                .InspectAsync(new RawLumaArtifactInspectionOptions(payloadPath, Width: 2, Height: 2));

            Assert.False(report.HasCompleteFrames);
            Assert.False(report.IsFrameAligned);
            Assert.Equal(0, report.CompleteFrameCount);
            Assert.Equal(3, report.TrailingBytes);
            Assert.Empty(report.Frames);
            Assert.Null(report.ContactSheet);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
