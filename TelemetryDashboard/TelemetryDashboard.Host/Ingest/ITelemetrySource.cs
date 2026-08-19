using System.Collections.Generic;
using System.Threading;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// A stream of raw frames feeding the ingest pump.
/// </summary>
/// <remarks>
/// The abstraction exists so the pump — parsing, routing, scoring, broadcasting, recording — is
/// written once and cannot behave differently for measured and synthetic input. It carries
/// <see cref="IsSimulated"/> rather than leaving the distinction to the caller, because the mark
/// has to survive all the way onto the wire and into the recording: a frame that loses it becomes
/// indistinguishable from a measurement the moment it is written down.
/// </remarks>
public interface ITelemetrySource : IAsyncDisposable
{
    /// <summary>Value written to the <c>source</c> field of every frame this source produces.</summary>
    string Origin { get; }

    /// <summary>Whether these frames are synthetic.</summary>
    bool IsSimulated { get; }

    /// <summary>One line describing the source for the startup banner.</summary>
    string Description { get; }

    /// <summary>Streams frames until cancellation.</summary>
    IAsyncEnumerable<RawPacket> ReadAsync(CancellationToken cancellationToken);
}
