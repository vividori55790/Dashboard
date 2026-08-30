using System;
using System.Linq;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// The group a channel's own name places it in, when its name places it in one.
/// </summary>
/// <remarks>
/// Two conventions agree on how a rig groups its channels, and this reads both. Prometheus's client
/// libraries build a metric name from an explicit <c>Namespace</c>, <c>Subsystem</c> and <c>Name</c>;
/// Sparkplug B gives a metric a folder path with <c>/</c> separators. Either way the leading
/// component is the group, and <c>dab.bus_voltage</c> belongs to <c>dab</c> the same way
/// <c>Inputs/0</c> belongs to <c>Inputs</c>.
/// <para>
/// <b>Underscore is not a delimiter here, and that is the decision worth arguing with.</b>
/// Prometheus joins its three parts with <c>_</c>, so <c>dab_bus_voltage</c> is the same channel
/// under that convention and this returns nothing for it. The reason is that <c>_</c> is also just
/// how a name spells a space: splitting on it turns <c>output_voltage</c> into a subsystem called
/// <c>output</c>, which is not a subsystem, it is an adjective. Half the rigs in the world would get
/// a grouping nobody made, and a wrong grouping is worse than none because the operator cannot see
/// that it was invented. A dot or a slash is somebody stating a hierarchy on purpose.
/// <b>Product decision, recorded rather than taken:</b> a per-rig setting naming the delimiter would
/// serve the underscore convention without guessing on behalf of the rigs that do not use it.
/// </para>
/// <para>
/// The device id is deliberately not a fallback. <c>PSFB-01</c> is which box the reading came from
/// and a subsystem is which part of the machine it describes; one box commonly carries several
/// subsystems, and several boxes commonly carry one. Substituting the first for the second would
/// produce a grouping that looks right on a one-device bench and is wrong on the plant.
/// </para>
/// </remarks>
public static class SubsystemName
{
    /// <summary>
    /// Separators that mean somebody declared a hierarchy, as against separating two words.
    /// </summary>
    private static readonly char[] Delimiters = ['.', '/', ':'];

    /// <summary>
    /// The leading component of the channel name, or null when the name declares no hierarchy.
    /// </summary>
    /// <remarks>
    /// Null and not <c>""</c> or <c>"default"</c>. A rig where nothing declares a group has no
    /// groups, and a bucket called "default" reads on a screen as a group an operator made.
    /// </remarks>
    public static string? From(string? channel)
    {
        string name = (channel ?? string.Empty).Trim();

        int cut = name.IndexOfAny(Delimiters);
        if (cut <= 0 || cut == name.Length - 1) return null;

        string head = name[..cut].Trim();

        // A single character is an initial, not a group, and a purely numeric head is an index --
        // "1.temperature" groups nothing. Both would show up as a column of one-row groups.
        if (head.Length < 2 || !head.Any(char.IsLetter)) return null;

        return head;
    }
}
