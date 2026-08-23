using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Ingest;

/// <summary>One channel a device was actually seen sending, and what its numbers looked like.</summary>
/// <param name="Tag">The frame tag it arrived under, because one device may send several.</param>
/// <param name="NodeId">The node the frames named, or the rule's default.</param>
/// <param name="Name">The name the device used — not the profile's name for it.</param>
/// <param name="Unit">The unit the device reported, which is the half most often wrong.</param>
public sealed record WireChannel(
    string Tag, string NodeId, string Name, string Unit, long Samples, double Minimum, double Maximum)
{
    /// <summary>The reading range, written the way an operator compares it to a band.</summary>
    public string Range => Minimum == Maximum
        ? Minimum.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)
        : $"{Minimum.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)}"
          + $"..{Maximum.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// What a device on the bench is really saying, recorded rather than assumed.
/// </summary>
/// <remarks>
/// Configuring the wire rules used to begin with knowing the answer: an operator had to already
/// know which names their firmware sends before they could write the file that renames them. This
/// is the other half — listen first, and write down what arrived.
/// <para>
/// It records the failures as carefully as the successes, because they are the informative ones. A
/// device whose frames begin <c>$DATA</c> rather than <c>$TELE</c> produces no channels at all, and
/// "nothing arrived" and "everything arrived under a tag nothing claims" look identical from a
/// chart. The unclaimed tags are counted so the report can tell them apart.
/// </para>
/// </remarks>
public sealed class WireSurvey
{
    private sealed class Entry
    {
        public long Samples;
        public double Minimum = double.MaxValue;
        public double Maximum = double.MinValue;
        public string Unit = string.Empty;
    }

    private readonly Dictionary<(string Tag, string Node, string Name), Entry> _channels = new();
    private readonly Dictionary<string, long> _unclaimed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _tags = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lines the source delivered, readable or not.</summary>
    public long Lines { get; private set; }

    /// <summary>Lines no rule and no fallback parser produced a reading from.</summary>
    public long UnreadableLines { get; private set; }

    /// <summary>Frame tags that did yield readings, e.g. <c>TELE</c>.</summary>
    public IReadOnlyCollection<string> Tags => _tags;

    /// <summary>Frame tags seen only on lines nothing could read, and how many lines each.</summary>
    public IReadOnlyDictionary<string, long> UnclaimedTags => _unclaimed;

    /// <summary>Every channel heard from, ordered by node and then by the name the device used.</summary>
    public IReadOnlyList<WireChannel> Channels =>
        _channels
            .Select(pair => new WireChannel(
                pair.Key.Tag, pair.Key.Node, pair.Key.Name, pair.Value.Unit,
                pair.Value.Samples, pair.Value.Minimum, pair.Value.Maximum))
            .OrderBy(c => c.Tag, StringComparer.Ordinal)
            .ThenBy(c => c.NodeId, StringComparer.Ordinal)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>Records one delivered line and whatever the ingest path made of it.</summary>
    public void Observe(RawPacket raw, IReadOnlyList<TelemetryPacket> parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        Lines++;
        string tag = TagOf(raw.RawLine);

        if (parsed.Count == 0)
        {
            UnreadableLines++;
            if (tag.Length > 0) _unclaimed[tag] = _unclaimed.TryGetValue(tag, out long seen) ? seen + 1 : 1;
            return;
        }

        if (tag.Length > 0) _tags.Add(tag);

        foreach (TelemetryPacket packet in parsed)
        {
            (string, string, string) key = (tag, packet.NodeId, packet.Variable);
            if (!_channels.TryGetValue(key, out Entry? entry))
            {
                entry = new Entry();
                _channels[key] = entry;
            }

            entry.Samples++;
            entry.Minimum = Math.Min(entry.Minimum, packet.Value);
            entry.Maximum = Math.Max(entry.Maximum, packet.Value);

            // Last one wins. A device that changes the unit of a channel mid-run is a fault of its
            // own, and it is visible in the report as a unit that does not match the readings.
            if (!string.IsNullOrWhiteSpace(packet.Unit)) entry.Unit = packet.Unit;
        }
    }

    /// <summary>The tag a prefix frame begins with, or empty when the line is not one.</summary>
    public static string TagOf(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return string.Empty;

        ReadOnlySpan<char> text = line.AsSpan().Trim();
        if (text.Length < 2 || text[0] != '$') return string.Empty;

        text = text[1..];
        int end = text.IndexOfAny(',', '*');
        if (end == 0) return string.Empty;

        return (end < 0 ? text : text[..end]).ToString();
    }
}
