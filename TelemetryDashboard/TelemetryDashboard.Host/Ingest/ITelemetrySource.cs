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

    /// <summary>Whether each sample decides its own origin, rather than this source deciding for it.</summary>
    /// <remarks>
    /// False for everything with a cable or a generator behind it: a serial port never produces
    /// synthetic data and a simulator never produces anything else, so the source is the
    /// authority and a sample claiming otherwise is a contradiction worth refusing.
    /// <para>
    /// True for a network source, where it is exactly backwards. Such a source does not know what
    /// it is carrying -- a peer may be relaying a bench rig, a simulator, or both -- so
    /// <c>IsSimulated => false</c> there means "this transport synthesises nothing", not "this
    /// data was measured". Reading it as the latter is what let a peer's synthetic stream be
    /// republished as measured: the sending host marked every frame simulated=true, and one hop
    /// later the same readings went out simulated=false. The mark this interface exists to carry
    /// was lost at precisely the boundary it matters at.
    /// </para>
    /// </remarks>
    bool SamplesCarryTheirOwnOrigin => false;

    /// <summary>One line describing the source for the startup banner.</summary>
    string Description { get; }

    /// <summary>Streams frames until cancellation.</summary>
    IAsyncEnumerable<RawPacket> ReadAsync(CancellationToken cancellationToken);
}
