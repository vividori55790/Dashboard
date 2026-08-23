using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Core.Ingest;

/// <summary>
/// A rules file written from what a device was heard saying.
/// </summary>
/// <remarks>
/// The point is to change the first step of configuring a real MCU from "know the answer and write
/// JSON" into "run it once and edit the file it wrote". Everything that arrived is in the file
/// under the name the device used, so nothing has to be remembered or guessed at.
/// <para>
/// What it will and will not decide is the whole design. A mapping is filled in only when the names
/// leave no choice — the device's name is the declared channel, or its last segment. Everything
/// else is written commented out, with the evidence beside it: the unit, the range, and which
/// declared channels those numbers would actually fit. A drafted file that guessed
/// <c>Vout -> psfb.output_voltage</c> from the shape of the words would look configured and be
/// wrong, and wrong-looking-configured is the state this whole feature exists to end.
/// </para>
/// <para>
/// Gains are the exception, and they are a derivation rather than a guess: millivolts to volts is
/// 0.001 by definition. <see cref="UnitScale"/> holds what that means and why it refuses anything
/// it cannot derive.
/// </para>
/// </remarks>
public static partial class RuleDraft
{
    /// <summary>Renders a rules file for <paramref name="survey"/>, judged against a profile.</summary>
    public static string Render(WireSurvey survey, MonitoringProfile? profile, string invocation = "")
    {
        ArgumentNullException.ThrowIfNull(survey);

        var draft = new StringBuilder();
        IReadOnlyList<WireChannel> channels = survey.Channels;
        Dictionary<string, string> mapped = MapByName(channels, profile);

        Preamble(draft, survey, profile, mapped, invocation);

        draft.AppendLine("{");
        draft.AppendLine($"  \"name\": \"{Sanitise(profile?.Id ?? "bench")}-wire\",");
        draft.AppendLine("  \"rules\": [");

        string[] tags = survey.Tags.Concat(survey.UnclaimedTags.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray();

        if (tags.Length == 0) tags = ["TELE"];

        for (int i = 0; i < tags.Length; i++)
        {
            Rule(draft, tags[i], channels, mapped, profile, survey);
            draft.AppendLine(i == tags.Length - 1 ? "    }" : "    },");
        }

        draft.AppendLine("  ]");
        draft.AppendLine("}");
        return draft.ToString();
    }

    private static void Rule(
        StringBuilder draft, string tag, IReadOnlyList<WireChannel> channels,
        Dictionary<string, string> mapped, MonitoringProfile? profile, WireSurvey survey)
    {
        WireChannel[] mine = channels
            .Where(c => string.Equals(c.Tag, tag, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        draft.AppendLine("    {");
        draft.AppendLine("      \"type\": \"prefix\",");
        draft.AppendLine($"      \"tag\": \"{tag}\",");
        draft.AppendLine("      \"port\": \"*\",");

        string[] nodes = mine.Select(c => c.NodeId).Distinct(StringComparer.Ordinal).ToArray();
        if (nodes.Length == 1 && nodes[0].Length > 0)
        {
            draft.AppendLine("      // Used only when a frame does not name a node itself.");
            draft.AppendLine($"      \"node\": \"{nodes[0]}\",");
        }

        if (mine.Length == 0)
        {
            draft.AppendLine(
                $"      // Nothing readable arrived under ${tag}: {survey.UnclaimedTags.GetValueOrDefault(tag)} "
                + "line(s) began with it and no rule claimed them.");
            draft.AppendLine("      // This rule is what claims them. Run sniff again with --rules");
            draft.AppendLine("      // pointing here and the channel names will be listed below.");
            draft.AppendLine("      \"channels\": { }");
            return;
        }

        draft.AppendLine("      \"channels\": {");
        foreach (WireChannel channel in mine) Channel(draft, channel, mapped, profile);
        draft.AppendLine("      }");
    }

    private static ProfileChannel? Declared(MonitoringProfile? profile, string id) =>
        profile?.Channels.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    private static string Sanitise(string text) =>
        new(text.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
}
