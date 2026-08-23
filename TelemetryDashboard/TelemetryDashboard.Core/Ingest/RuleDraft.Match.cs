using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Core.Ingest;

public static partial class RuleDraft
{
    /// <summary>
    /// The mappings the names themselves settle, and no others.
    /// </summary>
    /// <remarks>
    /// Two cases only: the device's name <em>is</em> the declared channel, or it is that channel's
    /// last segment — <c>output_voltage</c> for <c>psfb.output_voltage</c>. Both mean somebody
    /// already agreed on the name, either because the firmware came from this product's generator
    /// or because the operator has been here before.
    /// <para>
    /// There is deliberately no third case. <c>Vout</c> and <c>psfb.output_voltage</c> are the same
    /// rail and nothing about the words says so; a matcher confident enough to pair them is
    /// confident enough to pair <c>Vin</c> with it too.
    /// </para>
    /// </remarks>
    public static Dictionary<string, string> MapByName(
        IReadOnlyList<WireChannel> channels, MonitoringProfile? profile)
    {
        var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
        if (profile is null) return mapped;

        foreach (WireChannel channel in channels)
        {
            ProfileChannel? exact = profile.Channels.FirstOrDefault(
                c => string.Equals(c.Id, channel.Name, StringComparison.OrdinalIgnoreCase));

            ProfileChannel? tail = exact ?? profile.Channels.FirstOrDefault(
                c => string.Equals(LastSegment(c.Id), channel.Name, StringComparison.OrdinalIgnoreCase));

            if (tail is not null) mapped[channel.Name] = tail.Id;
        }

        return mapped;
    }

    /// <summary>
    /// Which declared channels a reading could be, best fit first.
    /// </summary>
    /// <remarks>
    /// This is the part worth having. A name says nothing, but 48200 mV against a band of 38..54 V
    /// says a great deal: convert the unit and there is one channel on this rig those numbers
    /// belong to, and the operator has their answer without opening the profile.
    /// <para>
    /// Ranked, because "it is inside the band" alone is nearly worthless. This profile's
    /// <c>grid.voltage</c> spans 0..440 V, so every voltage on the bench falls inside it and an
    /// unranked list puts the mains beside the rail with equal weight. The score is how far the
    /// readings sit from a channel's nominal, measured in widths of its own band: a narrow band
    /// centred on the reading scores near zero, a band wide enough to contain anything scores badly
    /// however true it is that the reading is inside it.
    /// </para>
    /// <para>
    /// Still candidates, never a decision. Two rails of one converter share a unit and a range, and
    /// choosing between them for the operator would be the same guess in a more confident voice.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ChannelCandidate> Candidates(
        WireChannel channel, MonitoringProfile? profile, IReadOnlyDictionary<string, string> alreadyMapped)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (profile is null) return [];

        var taken = new HashSet<string>(alreadyMapped.Values, StringComparer.OrdinalIgnoreCase);
        var fits = new List<ChannelCandidate>();
        double middle = (channel.Minimum + channel.Maximum) / 2.0;

        foreach (ProfileChannel declared in profile.Channels)
        {
            if (taken.Contains(declared.Id)) continue;

            double? gain = UnitScale.Between(channel.Unit, declared.Unit);
            if (gain is null) continue;

            double low = channel.Minimum * gain.Value;
            double high = channel.Maximum * gain.Value;
            if (high < declared.Minimum || low > declared.Maximum) continue;

            double width = Math.Abs(declared.Maximum - declared.Minimum);
            double reading = Math.Abs(middle * gain.Value);
            double reference = declared.Nominal != 0 || declared.Minimum <= 0
                ? declared.Nominal
                : (declared.Minimum + declared.Maximum) / 2.0;

            // Two terms, and the first dominates on purpose. How near the nominal a reading sits
            // is only meaningful once the band is narrow enough for "inside it" to mean something:
            // ranking by nominal distance alone put a 280 A battery band ahead of a 40 A input for
            // a 3.2 A reading, because zero happens to be its nominal.
            double tightness = reading > 1e-9 ? width / reading : double.MaxValue;
            double centred = width > 0 ? Math.Abs(middle * gain.Value - reference) / width : 0.0;

            fits.Add(new ChannelCandidate(declared, gain.Value, tightness + (2 * centred), tightness));
        }

        return fits.OrderBy(f => f.Score).ThenBy(f => f.Declared.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Whether the numbers leave one answer clearly ahead of the rest.
    /// </summary>
    /// <remarks>
    /// Two conditions, and both are refusals rather than tests. The band has to be tight enough
    /// that containing the reading is evidence at all, and it has to be at least twice as good as
    /// the runner-up — because writing out one of two equally good answers is a coin toss dressed
    /// as a recommendation, and the operator cannot tell which they were given.
    /// </remarks>
    public static bool IsDecisive(IReadOnlyList<ChannelCandidate> fits) =>
        fits.Count > 0
        && !fits[0].IsLoose
        && (fits.Count == 1 || fits[0].Score * 2 <= fits[1].Score);

    private static string LastSegment(string id)
    {
        int cut = id.LastIndexOf('.');
        return cut < 0 ? id : id[(cut + 1)..];
    }
}
