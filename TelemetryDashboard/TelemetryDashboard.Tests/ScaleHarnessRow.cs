using System.Globalization;

namespace TelemetryDashboard.Tests;

/// <summary>
/// One measured cardinality point. Every field is either something the process reported or is left
/// unset; there is no field this class fills in with a guess.
/// </summary>
public sealed class ScaleRow
{
    public ScaleRow(string subject, int cardinality, long managedBefore, long workingSetBefore)
    {
        Subject = subject;
        Cardinality = cardinality;
        ManagedBefore = managedBefore;
        WorkingSetBefore = workingSetBefore;
    }

    public string Subject { get; }
    public int Cardinality { get; }

    public long ManagedBefore { get; }
    public long WorkingSetBefore { get; }
    public long ManagedAfter { get; set; }
    public long WorkingSetAfter { get; set; }

    public double PopulateSeconds { get; set; }
    public long PopulateSamples { get; set; }
    public double SteadySeconds { get; set; }
    public long SteadySamples { get; set; }

    public int LiveCardinality { get; set; }
    public double ProbeMilliseconds { get; set; }
    public string ProbeLabel { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;

    /// <summary>Set when the run did not complete. A row with this set has no valid memory figures.</summary>
    public string? Failure { get; set; }

    public bool Completed => Failure is null;

    /// <summary>Managed bytes attributable to this run. Meaningless unless <see cref="Completed"/>.</summary>
    public long ManagedDelta => ManagedAfter - ManagedBefore;

    public long WorkingSetDelta => WorkingSetAfter - WorkingSetBefore;

    /// <summary>
    /// Managed bytes per channel including this run's fixed overhead. This over-states the marginal
    /// cost at small cardinalities; the honest marginal figure comes from differencing two rows.
    /// </summary>
    public double ManagedBytesPerChannel => Cardinality == 0 ? 0 : (double)ManagedDelta / Cardinality;

    public double SamplesPerSecond =>
        SteadySeconds <= 0 ? 0 : SteadySamples / SteadySeconds;

    public double PopulateSamplesPerSecond =>
        PopulateSeconds <= 0 ? 0 : PopulateSamples / PopulateSeconds;

    /// <summary>
    /// Marginal bytes per channel between two measured cardinalities. Fixed overhead cancels, so
    /// this is the figure that says what one more channel costs.
    /// </summary>
    public static double MarginalBytesPerChannel(ScaleRow smaller, ScaleRow larger)
    {
        if (!smaller.Completed || !larger.Completed) return double.NaN;
        if (larger.Cardinality == smaller.Cardinality) return double.NaN;

        return (double)(larger.ManagedDelta - smaller.ManagedDelta)
             / (larger.Cardinality - smaller.Cardinality);
    }

    public string ToCsv() => string.Join(",", new[]
    {
        Subject,
        Cardinality.ToString(CultureInfo.InvariantCulture),
        Completed ? ManagedDelta.ToString(CultureInfo.InvariantCulture) : "FAILED",
        Completed ? WorkingSetAfter.ToString(CultureInfo.InvariantCulture) : "FAILED",
        Completed ? ManagedBytesPerChannel.ToString("F1", CultureInfo.InvariantCulture) : "FAILED",
        Completed ? SamplesPerSecond.ToString("F0", CultureInfo.InvariantCulture) : "FAILED",
        Completed ? ProbeMilliseconds.ToString("F3", CultureInfo.InvariantCulture) : "FAILED",
        ProbeLabel,
        Note,
        Failure ?? string.Empty
    });

    public string ToReport()
    {
        if (!Completed)
        {
            return $"{Subject} @ {Cardinality:N0} channels: DID NOT COMPLETE - {Failure}";
        }

        return $"{Subject} @ {Cardinality:N0} channels {Note}\n"
             + $"  live cardinality reported : {LiveCardinality:N0}\n"
             + $"  managed heap delta        : {ManagedDelta:N0} bytes ({ManagedDelta / 1024.0 / 1024.0:F1} MB)\n"
             + $"  managed heap after        : {ManagedAfter:N0} bytes ({ManagedAfter / 1024.0 / 1024.0:F1} MB)\n"
             + $"  working set after         : {WorkingSetAfter:N0} bytes ({WorkingSetAfter / 1024.0 / 1024.0:F1} MB)\n"
             + $"  working set delta         : {WorkingSetDelta:N0} bytes ({WorkingSetDelta / 1024.0 / 1024.0:F1} MB)\n"
             + $"  bytes/channel (incl fixed): {ManagedBytesPerChannel:F1}\n"
             + $"  populate throughput       : {PopulateSamplesPerSecond:N0} samples/s over {PopulateSamples:N0} samples\n"
             + $"  sustained throughput      : {SamplesPerSecond:N0} samples/s over {SteadySamples:N0} samples\n"
             + $"  operator probe            : {ProbeMilliseconds:F3} ms - {ProbeLabel}";
    }
}
