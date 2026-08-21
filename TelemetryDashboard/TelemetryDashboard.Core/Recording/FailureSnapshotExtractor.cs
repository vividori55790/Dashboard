using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Recording;

/// <summary>
/// Extracts the window of telemetry surrounding a failure, for post-mortem analysis.
/// </summary>
/// <remarks>
/// The default window is the ten seconds leading up to the failure: what happened <em>before</em>
/// the fault is what explains it. A trailing margin is included so the operator can see how the
/// system responded.
/// <para>
/// It lived in <c>Infrastructure/Storage</c> and depends on nothing but <c>TelemetryPacket</c>,
/// which is why nothing could reach it: Core must not reference Infrastructure, so the endpoint
/// layer where an incident window is actually asked for could not have it. Its address was the
/// mistake, not its contents.
/// </para>
/// </remarks>
public sealed class FailureSnapshotExtractor
{
    /// <summary>Seconds captured before the failure instant.</summary>
    public double LeadSeconds { get; init; } = 10.0;

    /// <summary>Seconds captured after the failure instant.</summary>
    public double TrailSeconds { get; init; } = 2.0;

    /// <summary>Returns the packets bracketing <paramref name="failureTimestamp"/>, oldest first.</summary>
    public IReadOnlyList<TelemetryPacket> Extract10sFailureSnapshot(
        IEnumerable<TelemetryPacket> packets,
        DateTime failureTimestamp)
    {
        if (packets is null) return Array.Empty<TelemetryPacket>();

        DateTime start = failureTimestamp.AddSeconds(-Math.Abs(LeadSeconds));
        DateTime end = failureTimestamp.AddSeconds(Math.Abs(TrailSeconds));

        return packets
            .Where(p => p is not null && p.Timestamp >= start && p.Timestamp <= end)
            .OrderBy(p => p.Timestamp)
            .ToList();
    }

    /// <summary>Extracts a snapshot around each supplied failure instant.</summary>
    public IReadOnlyList<IReadOnlyList<TelemetryPacket>> ExtractAll(
        IEnumerable<TelemetryPacket> packets,
        IEnumerable<DateTime> failureTimestamps)
    {
        var materialized = packets?.ToList() ?? new List<TelemetryPacket>();

        return (failureTimestamps ?? Enumerable.Empty<DateTime>())
            .Select(ts => Extract10sFailureSnapshot(materialized, ts))
            .ToList();
    }
}
