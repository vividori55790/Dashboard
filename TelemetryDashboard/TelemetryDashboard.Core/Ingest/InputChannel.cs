using System;

namespace TelemetryDashboard.Core.Ingest;

/// <summary>One input a port is currently delivering, as of the last frame from it.</summary>
/// <param name="Port">The port it arrived on, which is the thing an operator can unplug.</param>
/// <param name="NodeId">The device the frame named.</param>
/// <param name="Channel">The channel name after routing — the profile's name when a rule maps it.</param>
/// <param name="ObservedMin">
/// Lowest finite reading seen, or null when none has been. Null rather than zero, and rather than
/// falling back to <paramref name="LastValue"/>: a range is what says whether a channel proposed as
/// a temperature has ever been anywhere a temperature could be, and a range invented from one
/// reading would answer that question with the reading itself.
/// </param>
/// <param name="ObservedMax">Highest finite reading seen, or null when none has been.</param>
public sealed record InputChannel(
    string Port, string NodeId, string Channel, string Unit,
    double LastValue, long Samples, DateTimeOffset FirstSeen, DateTimeOffset LastSeen,
    double? ObservedMin = null, double? ObservedMax = null)
{
    /// <summary>How long since this input last said anything.</summary>
    public TimeSpan Silence(DateTimeOffset now) => now - LastSeen;

    /// <summary>
    /// Mean interval between readings, or null while there has only ever been one.
    /// </summary>
    /// <remarks>
    /// Null rather than zero, and for the reason this project keeps restating: one sample
    /// establishes that a channel exists and nothing at all about its cadence. A rate of "0 Hz"
    /// beside a channel that has reported once is a measurement nobody made.
    /// </remarks>
    public TimeSpan? MeanInterval => Samples < 2
        ? null
        : (LastSeen - FirstSeen) / (Samples - 1);
}
