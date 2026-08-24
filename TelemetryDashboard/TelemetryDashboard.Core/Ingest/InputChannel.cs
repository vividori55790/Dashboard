using System;

namespace TelemetryDashboard.Core.Ingest;

/// <summary>One input a port is currently delivering, as of the last frame from it.</summary>
/// <param name="Port">The port it arrived on, which is the thing an operator can unplug.</param>
/// <param name="NodeId">The device the frame named.</param>
/// <param name="Channel">The channel name after routing — the profile's name when a rule maps it.</param>
public sealed record InputChannel(
    string Port, string NodeId, string Channel, string Unit,
    double LastValue, long Samples, DateTimeOffset FirstSeen, DateTimeOffset LastSeen)
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
