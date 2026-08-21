using TelemetryDashboard.Core.Firmware;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Infrastructure.Serial;
using TelemetryDashboard.Core.Resilience;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Covers Intel HEX decoding, bounded OTA retries, and circuit-breaker rate accounting.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public class FirmwareAndBreakerTests
{
    // A real Intel HEX image: 16 bytes at 0x0100, then 4 bytes at 0x0110, then EOF.
    private const string SampleHex =
        ":10010000214601360121470136007EFE09D2190140\n" +
        ":04011000FFFFFFFFEF\n" +
        ":00000001FF\n";

    [Fact]
    [Trait("Category", "Tier1")]
    public void IntelHex_DecodesRecordsIntoAddressedBinary()
    {
        FirmwareImage image = IntelHexParser.Parse(SampleHex);

        image.Format.Should().Be("hex");
        image.TotalBytes.Should().Be(20);
        image.StartAddress.Should().Be(0x0100);

        // 0x0100 and 0x0110 are contiguous, so they merge into a single run.
        image.Segments.Should().HaveCount(1);
        image.Segments[0].Data[0].Should().Be(0x21);
        image.Segments[0].Data[^1].Should().Be(0xFF);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void IntelHex_RejectsRecordWithBadChecksum()
    {
        string corrupted = SampleHex.Replace(":04011000FFFFFFFFEF", ":04011000FFFFFFFF00");

        Action parse = () => IntelHexParser.Parse(corrupted);

        parse.Should().Throw<FormatException>().WithMessage("*checksum*");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void IntelHex_HonoursExtendedLinearAddressRecords()
    {
        // 0x04 record sets the upper 16 bits to 0x0800 => data lands at 0x08000000.
        string hex = ":020000040800F2\n:04000000DEADBEEFC4\n:00000001FF\n";

        FirmwareImage image = IntelHexParser.Parse(hex);

        image.StartAddress.Should().Be(0x08000000);
        image.TotalBytes.Should().Be(4);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task OtaFlasher_DecodesHexRatherThanStreamingAsciiText()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fw-{Guid.NewGuid():N}.hex");
        await File.WriteAllTextAsync(path, SampleHex);

        try
        {
            var sent = new List<byte>();
            var flasher = new EdgeMcuOtaFlasher { ChunkPacing = TimeSpan.Zero };

            OtaFlashResult result = await flasher.FlashFirmwareAsync("COM3", path, chunk =>
            {
                sent.AddRange(chunk);
                return Task.FromResult(true);
            });

            result.Success.Should().BeTrue();
            // 20 decoded bytes, not the 60+ ASCII characters of the file.
            result.BytesSent.Should().Be(20);
            sent.Should().HaveCount(20);
            sent[0].Should().Be(0x21);
            sent.Should().NotContain((byte)':', "the ':' record marker must never reach the device");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task OtaFlasher_StopsAfterRetryLimitInsteadOfLoopingForever()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fw-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, new byte[512]);

        try
        {
            int attempts = 0;
            var flasher = new EdgeMcuOtaFlasher
            {
                MaxRetriesPerChunk = 3,
                RetryDelay = TimeSpan.Zero,
                ChunkPacing = TimeSpan.Zero
            };

            // A device that never acknowledges previously spun this loop indefinitely.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            OtaFlashResult result = await flasher.FlashFirmwareAsync("COM3", path, _ =>
            {
                attempts++;
                return Task.FromResult(false);
            }, timeout.Token);

            result.Success.Should().BeFalse();
            attempts.Should().Be(3, "each chunk gets exactly MaxRetriesPerChunk attempts");
            result.Message.Should().Contain("재시도");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task OtaFlasher_RefusesToTransmitACorruptHexFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fw-{Guid.NewGuid():N}.hex");
        await File.WriteAllTextAsync(path, ":04011000FFFFFFFF00\n");

        try
        {
            bool anyChunkSent = false;
            var flasher = new EdgeMcuOtaFlasher { ChunkPacing = TimeSpan.Zero };

            OtaFlashResult result = await flasher.FlashFirmwareAsync("COM3", path, _ =>
            {
                anyChunkSent = true;
                return Task.FromResult(true);
            });

            result.Success.Should().BeFalse();
            anyChunkSent.Should().BeFalse("a corrupt image must abort before any byte reaches the device");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CircuitBreaker_IsolatesChannelExceedingRateLimit()
    {
        var breaker = new TelemetryCircuitBreaker { MaxAllowedRatePerSec = 100 };
        string? isolated = null;
        breaker.ChannelIsolated += (_, channel) => isolated = channel;

        bool blocked = false;
        for (int i = 0; i < 200 && !blocked; i++)
        {
            blocked = !breaker.AllowPacketProcessing("flood");
        }

        blocked.Should().BeTrue();
        isolated.Should().Be("flood");
        breaker.IsChannelIsolated("flood").Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CircuitBreaker_RateAccountingStaysFastUnderFlood()
    {
        var breaker = new TelemetryCircuitBreaker { MaxAllowedRatePerSec = int.MaxValue };

        for (int i = 0; i < 50_000; i++)
        {
            breaker.RecordPacket("burst");
        }

        // Reading the clamp state is what the UI does every frame. The previous implementation
        // scanned all 50,000 timestamps per read.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1_000; i++)
        {
            _ = breaker.IsUiResourceClamped;
            _ = breaker.SubsampleRatio;
        }
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(500);
        breaker.CurrentAggregateRate.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CircuitBreaker_SubsampleRatioScalesWithLoad()
    {
        var breaker = new TelemetryCircuitBreaker { UiClampRatePerSec = 100, MaxAllowedRatePerSec = int.MaxValue };

        breaker.SubsampleRatio.Should().Be(1);

        for (int i = 0; i < 1_000; i++) breaker.RecordPacket("busy");

        breaker.IsUiResourceClamped.Should().BeTrue();
        breaker.SubsampleRatio.Should().BeGreaterThan(1);
    }
}
