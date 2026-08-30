using System;

namespace TelemetryDashboard.Core.Cluster;

/// <summary>
/// The two ways a sample is recognised: by the counter its sender assigned, or by what it is.
/// </summary>
/// <remarks>
/// Split from the state and the counters because this is the part with a judgement in it. The two
/// paths answer the question differently and fail differently: a counter is what the sender
/// actually assigned and settles it outright, while an identity is inferred and works on anything
/// with a node, a channel and an instant -- which is what a reading pulled back out of an archive
/// has, and all it has.
/// </remarks>
public sealed partial class DuplicateFilter
{
    /// <summary>Whether to take a sample identified by what it is rather than by a counter.</summary>
    /// <remarks>
    /// For samples recovered out of a peer's archive after an outage. They carry no sequence --
    /// an archive stores a reading, not the frame that delivered it -- so the counter path below
    /// would admit them unchecked, and a fill run twice would double every recovered sample. The
    /// natural key is exact: one node's one channel at one instant is one observation.
    /// <para>
    /// The instant is formatted round-trip rather than hashed. A hash would be shorter, and a
    /// collision would silently discard a real sample -- the failure direction that destroys an
    /// observation rather than the one that inflates a total. At a bounded window the saving is
    /// not worth buying with that.
    /// </para>
    /// </remarks>
    public bool AdmitObservation(string nodeId, string variable, DateTime observedUtc)
    {
        Window window = _senders.GetOrAdd($"obs{KeySeparator}{nodeId}", _ => new Window(), out _);
        string key = $"{variable}{KeySeparator}{observedUtc:O}";

        lock (_gate)
        {
            if (!window.Observations.Add(key))
            {
                _duplicates++;
                return false;
            }

            window.ObservationOrder.Enqueue(key);
            while (window.ObservationOrder.Count > _window)
            {
                window.Observations.Remove(window.ObservationOrder.Dequeue());
            }

            _admitted++;
            return true;
        }
    }

    private bool AdmitSequence(string nodeId, string epoch, long sequence)
    {
        // The separator is the escape, not the byte. This line carried a literal 0x1F for two
        // commits -- invisible in an editor, in a diff and in review, surviving only until
        // something normalises the file. ArchitectureRuleTests.NoSourceFileCarriesARawControl
        // Character now catches it, because a comment in one file is not a check on another.
        Window window = _senders.GetOrAdd(
            $"seq{KeySeparator}{nodeId}{KeySeparator}{epoch}", _ => new Window(), out _);

        lock (_gate)
        {
            if (!window.Seen.Add(sequence))
            {
                _duplicates++;
                return false;
            }

            window.Order.Enqueue(sequence);
            while (window.Order.Count > _window)
            {
                window.Seen.Remove(window.Order.Dequeue());
            }

            _admitted++;
            return true;
        }
    }
}
