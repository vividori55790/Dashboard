using TelemetryDashboard.Core.Analytics.Detectors;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Signals whose correct answer is known before the detector sees them, and the harness that runs
/// one through a detector.
/// </summary>
/// <remarks>
/// Deterministic on purpose — every "noise" here is a fixed alternation rather than a random draw.
/// A detector test seeded from a random generator asserts a property of one sample of one
/// distribution and fails on somebody else's machine at 3am; these assert arithmetic, so a failure
/// means the detector changed rather than that the dice did.
/// </remarks>
internal static class DetectorSignals
{
    /// <summary>Start of every generated series, so timestamps in a failure message are readable.</summary>
    public static readonly DateTime Origin = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>A flat channel with a fixed +/- wobble, the closest thing here to calm telemetry.</summary>
    public static double[] Wobble(int count, double centre, double amplitude)
    {
        var values = new double[count];
        for (int i = 0; i < count; i++) values[i] = centre + (i % 2 == 0 ? amplitude : -amplitude);
        return values;
    }

    /// <summary>A perfectly constant channel: no spread at all for a scale-based detector to use.</summary>
    public static double[] Constant(int count, double value) => Enumerable.Repeat(value, count).ToArray();

    /// <summary>A straight ramp, one step per sample.</summary>
    public static double[] Ramp(int count, double start, double stepPerSample) =>
        Enumerable.Range(0, count).Select(i => start + i * stepPerSample).ToArray();

    /// <summary>Runs a series through a detector, one verdict per sample, at a fixed cadence.</summary>
    public static IReadOnlyList<DetectorVerdict> Run(
        IChannelDetector detector, string channel, IEnumerable<double> values, TimeSpan? step = null)
    {
        TimeSpan cadence = step ?? TimeSpan.FromMilliseconds(100);
        DateTime at = Origin;

        var verdicts = new List<DetectorVerdict>();
        foreach (double value in values)
        {
            verdicts.Add(detector.Evaluate(channel, value, at));
            at += cadence;
        }
        return verdicts;
    }

    /// <summary>How many of these verdicts were both reached and anomalous.</summary>
    public static int Flagged(this IEnumerable<DetectorVerdict> verdicts) =>
        verdicts.Count(v => v is { HasVerdict: true, IsAnomaly: true });

    /// <summary>How many carried no judgement at all.</summary>
    public static int Unjudged(this IEnumerable<DetectorVerdict> verdicts) =>
        verdicts.Count(v => !v.HasVerdict);
}
