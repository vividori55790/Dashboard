using System.Diagnostics;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Storage;
using Xunit.Abstractions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Measures what the shipped <see cref="SqliteDataLogger"/> actually does on this machine, so the
/// case for a different store rests on numbers rather than on the folklore that "SQLite is slow".
/// </summary>
/// <remarks>
/// Nothing here asserts a throughput figure. A machine-specific rate baked into an assertion is a
/// test that fails on a slower laptop and tells you nothing about the code, so the assertions cover
/// only what must be true regardless of speed (every row committed, the file grew) and the numbers
/// are reported through <see cref="ITestOutputHelper"/>.
/// <para>
/// Run with: <c>dotnet test --filter "FullyQualifiedName~TieredStorageBaseline" --logger "console;verbosity=detailed"</c>
/// </para>
/// </remarks>
public sealed class TieredStorageBaselineBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public TieredStorageBaselineBenchmarkTests(ITestOutputHelper output) => _output = output;

    /// <summary>Slowly varying telemetry, the shape this store is actually fed.</summary>
    private static IEnumerable<TelemetryPacket> Packets(int count, int channels, DateTime startUtc)
    {
        var random = new Random(20260819);
        var walk = new double[channels];
        for (int i = 0; i < channels; i++) walk[i] = 20.0 + i;

        for (int i = 0; i < count; i++)
        {
            int channel = i % channels;
            walk[channel] += (random.NextDouble() - 0.5) * 0.05;
            yield return new TelemetryPacket(
                $"node-{channel / 8:D3}",
                $"ch{channel:D3}",
                walk[channel],
                "C",
                startUtc.AddMilliseconds(i / (double)channels));
        }
    }

    private static long FileBytes(string path) =>
        new[] { path, path + "-wal", path + "-journal" }
            .Where(File.Exists)
            .Sum(p => new FileInfo(p).Length);

    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task SqliteDataLogger_BatchedInsertRate()
    {
        foreach (int batchSize in new[] { 512, 10_000 })
        {
            using var workspace = new TempWorkspace();
            string path = workspace.File($"baseline-{batchSize}.db");
            using var logger = new SqliteDataLogger(path);

            const int total = 200_000;
            var batches = Packets(total, 64, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .Chunk(batchSize)
                .ToList();

            var stopwatch = Stopwatch.StartNew();
            foreach (TelemetryPacket[] batch in batches)
            {
                await logger.WriteBatchAsync(batch);
            }
            stopwatch.Stop();

            long bytes = FileBytes(path);
            _output.WriteLine(
                $"BATCHED batch={batchSize,6}  rows={total}  " +
                $"{total / stopwatch.Elapsed.TotalSeconds,10:N0} rows/s  " +
                $"{stopwatch.Elapsed.TotalSeconds,6:F2} s  " +
                $"file={bytes / 1024.0 / 1024.0,7:F2} MiB  {bytes / (double)total,5:F1} B/row");

            logger.WrittenCount.Should().Be(total);
        }
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task SqliteDataLogger_UnbatchedInsertRate()
    {
        using var workspace = new TempWorkspace();
        string path = workspace.File("baseline-unbatched.db");
        using var logger = new SqliteDataLogger(path);

        const int total = 2_000;
        List<TelemetryPacket> packets =
            Packets(total, 64, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).ToList();

        var stopwatch = Stopwatch.StartNew();
        foreach (TelemetryPacket packet in packets)
        {
            await logger.WriteAsync(packet);
        }
        stopwatch.Stop();

        _output.WriteLine(
            $"UNBATCHED (one transaction per packet)  rows={total}  " +
            $"{total / stopwatch.Elapsed.TotalSeconds,10:N0} rows/s  " +
            $"{stopwatch.Elapsed.TotalSeconds,6:F2} s  " +
            $"file={FileBytes(path) / 1024.0,8:F1} KiB");

        logger.WrittenCount.Should().Be(total);
    }

    /// <summary>One million rows, written the way the drain writes them, measured end to end.</summary>
    /// <remarks>
    /// A million is the target's one-second workload. Measuring it directly rather than
    /// extrapolating from a smaller run is the point: index maintenance and page splits are not
    /// linear, so an extrapolated file size would be an estimate presented as a measurement.
    /// </remarks>
    [Fact]
    [Trait("Category", "Benchmark")]
    public async Task SqliteDataLogger_OneMillionSamples_RateAndFileGrowth()
    {
        using var workspace = new TempWorkspace();
        string path = workspace.File("baseline-1m.db");
        using var logger = new SqliteDataLogger(path);

        const int total = 1_000_000;
        var stopwatch = Stopwatch.StartNew();
        foreach (TelemetryPacket[] batch in
                 Packets(total, 1_000, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Chunk(10_000))
        {
            await logger.WriteBatchAsync(batch);
        }
        stopwatch.Stop();

        long bytes = FileBytes(path);
        _output.WriteLine(
            $"ONE MILLION rows over 1000 channels: {stopwatch.Elapsed.TotalSeconds:F2} s  " +
            $"{total / stopwatch.Elapsed.TotalSeconds:N0} rows/s  " +
            $"file={bytes / 1024.0 / 1024.0:F2} MiB  {bytes / (double)total:F1} B/row  " +
            $"=> {bytes / (double)total * 86_400_000_000.0 / 1024 / 1024 / 1024 / 1024:F1} TiB/day at 1e6 samples/s");

        logger.WrittenCount.Should().Be(total);
        bytes.Should().BeGreaterThan(0);
    }
}
