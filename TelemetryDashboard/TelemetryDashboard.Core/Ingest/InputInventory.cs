using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Resilience;

namespace TelemetryDashboard.Core.Ingest;

/// <summary>
/// What each port is actually delivering, right now, grouped the way a cable is.
/// </summary>
/// <remarks>
/// The question an operator asks first — "what is this thing sending me?" — had no answer that
/// stayed answered. <c>WireSurvey</c> answers it once, for a fixed window, and keys on the frame
/// tag rather than the port, which is right for drafting a rules file and wrong for a live view:
/// two devices on two ports speaking the same tag collapse into one row. <c>ChannelSilenceWatch</c>
/// knows each channel's cadence and last sighting but not its value, its unit or where it came in.
/// <para>
/// So this keys on the thing that can be unplugged. Port first, because that is the unit an
/// operator reasons about — the cable, the adapter, the bench instrument — and because ToDo item 4
/// asks for the inputs of each system to be visible per system rather than as one flat list.
/// </para>
/// <para>
/// Bounded, through <see cref="BoundedChannelRegistry{TState}"/>, for the same reason every other
/// per-channel store here is: a device that invents a channel name per frame would otherwise grow
/// this without limit, and the cardinality is reported rather than silently capped.
/// </para>
/// </remarks>
public sealed class InputInventory
{
    private sealed class Entry
    {
        public string Port = string.Empty;
        public string NodeId = string.Empty;
        public string Channel = string.Empty;
        public string Unit = string.Empty;
        public double LastValue;
        public long Samples;
        public DateTimeOffset FirstSeen;
        public DateTimeOffset LastSeen;
    }

    private readonly BoundedChannelRegistry<Entry> _entries;
    private const char KeySeparator = '\u001f';

    private readonly object _gate = new();

    /// <param name="capacity">
    /// Most distinct (port, node, channel) rows held at once. The default is generous because the
    /// real number is small — a rig has the inputs it has — and the ceiling exists for the device
    /// that is misbehaving rather than for the one that is working.
    /// </param>
    public InputInventory(int capacity = 4096)
    {
        _entries = new BoundedChannelRegistry<Entry>(capacity, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Distinct inputs currently held.</summary>
    public int Count => _entries.Count;

    /// <summary>Rows dropped because <see cref="InputInventory(int)"/>'s ceiling was reached.</summary>
    /// <remarks>
    /// Exposed rather than kept, because a view that silently shows a subset of the inputs is the
    /// coverage problem this project's architecture document opens with, in miniature.
    /// </remarks>
    public long Evictions => _entries.Evictions;

    /// <summary>Records one routed reading against the port it arrived on.</summary>
    public void Observe(RawPacket raw, TelemetryPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        string port = string.IsNullOrWhiteSpace(raw.PortName) ? "(unnamed)" : raw.PortName;
        string node = packet.NodeId;
        string channel = packet.Variable;
        if (channel.Length == 0) return;

        DateTimeOffset seen = packet.Timestamp == default
            ? DateTimeOffset.UtcNow
            : new DateTimeOffset(packet.Timestamp.ToUniversalTime(), TimeSpan.Zero);

        // A separator no channel name can contain, so (COM3, A, BC) and (COM3, AB, C) cannot
        // collide into one row. Named rather than typed inline: it is invisible in an editor,
        // and an invisible character in a key is the kind of thing a later tidy-up deletes.
        string key = string.Join(KeySeparator, port, node, channel);

        lock (_gate)
        {
            Entry entry = _entries.GetOrAdd(key, _ => new Entry
            {
                Port = port,
                NodeId = node,
                Channel = channel,
                FirstSeen = seen
            }, out _);

            entry.Samples++;
            entry.LastValue = packet.Value;
            entry.LastSeen = seen;

            // Last one wins, and an empty unit never overwrites a known one: a device that omits
            // the unit on some frames should not make the column flicker.
            if (!string.IsNullOrWhiteSpace(packet.Unit)) entry.Unit = packet.Unit;
        }
    }

    /// <summary>Every input, ordered by port and then by channel.</summary>
    public IReadOnlyList<InputChannel> Channels()
    {
        lock (_gate)
        {
            return _entries.Snapshot()
                .Select(e => new InputChannel(
                    e.Port, e.NodeId, e.Channel, e.Unit, e.LastValue, e.Samples, e.FirstSeen, e.LastSeen))
                .OrderBy(c => c.Port, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.NodeId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Channel, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>The ports heard from, in the order <see cref="Channels"/> presents them.</summary>
    public IReadOnlyList<string> Ports() =>
        Channels().Select(c => c.Port).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Forgets everything, for a session that has been disconnected and reopened.</summary>
    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }
}
