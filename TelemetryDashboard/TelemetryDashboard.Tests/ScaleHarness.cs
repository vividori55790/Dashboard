using System.Diagnostics;
using System.Globalization;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Resilience;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Measurement primitives for the per-channel-state scale study.
/// </summary>
/// <remarks>
/// Everything this class reports is read from the running process — <see cref="GC.GetTotalMemory"/>
/// after a blocking collection, and <see cref="Process.WorkingSet64"/>. Nothing is modelled or
/// extrapolated. Where a figure could not be obtained (an out-of-memory abort, a run that was cut
/// short) the row records the failure instead of a number, because a plausible-looking estimate in
/// a memory table is indistinguishable from a measurement once it is written down.
/// <para>
/// Channel names are generated on the fly and never retained by the harness, so the only thing
/// holding them alive is the component under test. An array of a million names in the harness would
/// have shown up as component overhead.
/// </para>
/// </remarks>
public static class ScaleHarness
{
    /// <summary>Set to "1" to run the heavy cardinality sweeps. Absent, the sweeps do not run.</summary>
    public const string EnableVariable = "TELEMETRY_SCALE_HARNESS";

    /// <summary>Optional path for the append-only results log.</summary>
    public const string OutputVariable = "TELEMETRY_SCALE_OUTPUT";

    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal);

    /// <summary>The owner's target topology: 1000 computers x 1000 sensors.</summary>
    public static string ChannelName(int index)
    {
        int host = index / 1000;
        int sensor = index % 1000;
        return string.Create(CultureInfo.InvariantCulture, $"host{host:D4}.sensor{sensor:D4}");
    }

    /// <summary>Blocking full collection, then the settled managed heap size.</summary>
    public static long SettledManagedBytes()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    public static long WorkingSetBytes()
    {
        using var self = Process.GetCurrentProcess();
        self.Refresh();
        return self.WorkingSet64;
    }

    /// <summary>
    /// Feeds <paramref name="cardinality"/> distinct channels through a real engine and reports what
    /// the process actually did.
    /// </summary>
    /// <param name="rounds">
    /// Passes over the whole channel set. The first pass creates state; later passes exercise the
    /// scored path, which only runs once a channel has <see cref="TelemetryMlAnalyticsEngine.MinimumSamples"/>
    /// samples. The final pass is timed on its own as sustained throughput.
    /// </param>
    public static ScaleRow MeasureEngine(int cardinality, int rounds = 6, int windowSize = 50)
    {
        long managedBefore = SettledManagedBytes();
        long workingSetBefore = WorkingSetBytes();

        var engine = new TelemetryMlAnalyticsEngine(windowSize, sampleRateHz: 20.0);
        var row = new ScaleRow("TelemetryMlAnalyticsEngine", cardinality, managedBefore, workingSetBefore);

        try
        {
            var populate = Stopwatch.StartNew();
            for (int i = 0; i < cardinality; i++)
            {
                engine.AnalyzeChannel(ChannelName(i), 20.0 + (i % 7));
            }
            populate.Stop();
            row.PopulateSeconds = populate.Elapsed.TotalSeconds;
            row.PopulateSamples = cardinality;

            // Intermediate rounds bring every channel past warm-up so the timed round is scored work.
            for (int r = 1; r < rounds - 1; r++)
            {
                for (int i = 0; i < cardinality; i++)
                {
                    engine.AnalyzeChannel(ChannelName(i), 20.0 + ((i + r) % 11) * 0.25);
                }
            }

            var steady = Stopwatch.StartNew();
            for (int i = 0; i < cardinality; i++)
            {
                engine.AnalyzeChannel(ChannelName(i), 20.0 + (i % 13) * 0.5);
            }
            steady.Stop();
            row.SteadySeconds = steady.Elapsed.TotalSeconds;
            row.SteadySamples = cardinality;

            row.LiveCardinality = engine.TrackedChannelCount;

            // The operator-facing read. MlAnalyticsDialog calls this to render the anomaly list.
            var probe = Stopwatch.StartNew();
            int anomalies = engine.RecentAnomalies.Count;
            probe.Stop();
            row.ProbeMilliseconds = probe.Elapsed.TotalMilliseconds;
            row.ProbeLabel = $"RecentAnomalies ({anomalies} entries)";

            row.ManagedAfter = SettledManagedBytes();
            row.WorkingSetAfter = WorkingSetBytes();
        }
        catch (OutOfMemoryException ex)
        {
            row.Failure = "OutOfMemoryException: " + ex.Message;
        }

        GC.KeepAlive(engine);
        return row;
    }

    /// <summary>
    /// Feeds <paramref name="cardinality"/> distinct channels through a real breaker.
    /// </summary>
    /// <param name="packetsPerChannel">
    /// Packets pushed per channel inside one rate window. The breaker queues a timestamp per packet
    /// and only prunes on the next touch of that same channel, so this is the axis on which its
    /// memory scales with rate rather than with cardinality.
    /// </param>
    public static ScaleRow MeasureBreaker(int cardinality, int packetsPerChannel = 1)
    {
        long managedBefore = SettledManagedBytes();
        long workingSetBefore = WorkingSetBytes();

        var breaker = new TelemetryCircuitBreaker { MaxAllowedRatePerSec = int.MaxValue };
        var row = new ScaleRow("TelemetryCircuitBreaker", cardinality, managedBefore, workingSetBefore)
        {
            Note = $"{packetsPerChannel} packet(s)/channel"
        };

        try
        {
            var populate = Stopwatch.StartNew();
            for (int p = 0; p < packetsPerChannel; p++)
            {
                for (int i = 0; i < cardinality; i++)
                {
                    breaker.AllowPacketProcessing(ChannelName(i));
                }
            }
            populate.Stop();
            row.PopulateSeconds = populate.Elapsed.TotalSeconds;
            row.PopulateSamples = (long)cardinality * packetsPerChannel;
            row.SteadySeconds = row.PopulateSeconds;
            row.SteadySamples = row.PopulateSamples;

            // The operator-facing read. The UI consults this to decide whether to subsample, and it
            // walks every tracker in the dictionary taking a lock on each one.
            var probe = Stopwatch.StartNew();
            bool clamped = breaker.IsUiResourceClamped;
            probe.Stop();
            row.ProbeMilliseconds = probe.Elapsed.TotalMilliseconds;
            row.ProbeLabel = $"IsUiResourceClamped (={clamped})";

            row.ManagedAfter = SettledManagedBytes();
            row.WorkingSetAfter = WorkingSetBytes();
        }
        catch (OutOfMemoryException ex)
        {
            row.Failure = "OutOfMemoryException: " + ex.Message;
        }

        GC.KeepAlive(breaker);
        return row;
    }

    /// <summary>Appends a row to the results log, if one was configured.</summary>
    public static void Record(ScaleRow row)
    {
        string? path = Environment.GetEnvironmentVariable(OutputVariable);
        if (string.IsNullOrWhiteSpace(path)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, row.ToCsv() + Environment.NewLine);
    }
}
