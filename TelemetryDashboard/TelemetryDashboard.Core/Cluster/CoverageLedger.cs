using System;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.Core.Cluster;

/// <summary>
/// Tracks which nodes were expected and which were actually heard from.
/// </summary>
/// <remarks>
/// Kept separate from the data on purpose. Counting the samples that arrived can only ever describe
/// the nodes that sent some; the question that matters is about the ones that did not, and no
/// amount of arithmetic over the received data can answer it.
///
/// Expectation comes from two places, and it needs both. Declared expectation catches a node that
/// has never once come up, which learned expectation cannot see. Learned expectation — anything
/// heard from is expected from then on — catches the far commoner case of a node that worked for
/// months and then stopped, without anyone having to maintain a list. Relying on declaration alone
/// means an unlisted node's death is invisible; relying on learning alone means a node that never
/// started is invisible.
///
/// <see cref="KnownNodes"/> exists so the learned set can be persisted. Without that, a restart
/// forgets that a node ever existed, and its absence becomes undetectable again at exactly the
/// moment an operator restarts the hub to investigate why data is missing.
/// </remarks>
public sealed partial class CoverageLedger
{
    private sealed class Entry
    {
        public DateTimeOffset? LastHeard;
        public long Samples;
        public bool Declared;
    }

    private readonly Dictionary<string, Entry> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Default silence threshold. Deliberately generous: a false alarm trains people to ignore alarms.</summary>
    public static readonly TimeSpan DefaultSilenceThreshold = TimeSpan.FromSeconds(30);

    /// <param name="silenceThreshold">How long without a sample before a node counts as silent.</param>
    /// <param name="clock">Injected so silence can be tested without waiting for it.</param>
    public CoverageLedger(TimeSpan? silenceThreshold = null, Func<DateTimeOffset>? clock = null)
    {
        SilenceThreshold = silenceThreshold ?? DefaultSilenceThreshold;

        if (SilenceThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(silenceThreshold),
                "A threshold of zero would report every node as silent between two samples.");
        }

        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>How long a node may go without sending before it is reported as missing.</summary>
    public TimeSpan SilenceThreshold { get; }

    /// <summary>Declares a node as expected before it has ever been heard from.</summary>
    /// <remarks>
    /// This is the only way a node that has never started can be reported as missing. Use it for a
    /// configured fleet, and to restore the learned set after a restart.
    /// <para>
    /// <paramref name="lastHeard"/> is for the restore case and changes what the node is reported
    /// as. Without it a node remembered from a previous run comes back as never seen, which reads
    /// as hardware that has never worked — when what actually happened is that it worked until
    /// yesterday. It does not count as a sample: this process has received none, and inventing one
    /// would put a reading in the record that no device sent.
    /// </para>
    /// </remarks>
    public void Expect(string nodeId, DateTimeOffset? lastHeard = null)
    {
        if (string.IsNullOrWhiteSpace(nodeId)) return;

        lock (_gate)
        {
            if (!_nodes.TryGetValue(nodeId, out Entry? entry))
            {
                entry = new Entry();
                _nodes[nodeId] = entry;
            }

            entry.Declared = true;

            // Newest wins, the same rule RecordSample uses: a restored stamp must never move a
            // node that has already reported in this run backwards into silence.
            if (lastHeard is { } stamp && (entry.LastHeard is null || stamp > entry.LastHeard))
            {
                entry.LastHeard = stamp;
            }
        }
    }

    /// <summary>Records that a node contributed a sample. A node heard once is expected thereafter.</summary>
    public void RecordSample(string nodeId, DateTimeOffset? observedAt = null)
    {
        if (string.IsNullOrWhiteSpace(nodeId)) return;

        DateTimeOffset stamp = observedAt ?? _clock();

        lock (_gate)
        {
            if (!_nodes.TryGetValue(nodeId, out Entry? entry))
            {
                entry = new Entry();
                _nodes[nodeId] = entry;
            }

            entry.Samples++;

            // Keep the newest. Out-of-order arrival is normal across a network, and letting a late
            // sample move the clock backwards would report a live node as freshly silent.
            if (entry.LastHeard is null || stamp > entry.LastHeard) entry.LastHeard = stamp;
        }
    }

    /// <summary>Stops expecting a node, for one that was decommissioned rather than lost.</summary>
    /// <remarks>
    /// Necessary, and dangerous: it is the one call that can make a genuinely missing node stop
    /// being reported. It is deliberately explicit for that reason, and never happens automatically
    /// on silence — a node going quiet is the event worth knowing about, not a reason to forget it.
    /// </remarks>
    public bool Retire(string nodeId)
    {
        lock (_gate) return _nodes.Remove(nodeId);
    }
}
