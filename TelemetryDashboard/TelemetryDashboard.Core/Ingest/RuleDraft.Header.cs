using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Core.Ingest;

public static partial class RuleDraft
{
    /// <summary>
    /// The header: what was heard, and what the profile is still waiting for.
    /// </summary>
    /// <remarks>
    /// Written as comments in the file itself rather than only to the console, because the console
    /// output scrolls away and the file is what the operator has open while they edit it. The
    /// checklist of declared channels is the reference they would otherwise be looking up, and
    /// having it beside the blanks is most of what makes the file fillable in one sitting.
    /// </remarks>
    private static void Preamble(
        StringBuilder draft, WireSurvey survey, MonitoringProfile? profile,
        IReadOnlyDictionary<string, string> mapped, string invocation)
    {
        if (invocation.Length > 0) draft.AppendLine($"// Written by: {invocation}");

        draft.AppendLine(
            $"// Heard {survey.Lines:N0} line(s); {survey.Channels.Count} channel(s) arrived, "
            + $"{survey.UnreadableLines:N0} line(s) could not be read.");
        draft.AppendLine("//");
        draft.AppendLine("// Every name the device sent is below. Where the readings left only one");
        draft.AppendLine("// sensible answer the line is written out for you and commented; delete");
        draft.AppendLine("// the // to accept it, or change the channel name first.");

        if (profile is null)
        {
            draft.AppendLine("// No profile was named, so nothing here is checked against one.");
            return;
        }

        var taken = new HashSet<string>(mapped.Values, StringComparer.OrdinalIgnoreCase);
        draft.AppendLine("//");
        draft.AppendLine($"// What profile '{profile.Id}' declares, and what is spoken for:");

        foreach (ProfileChannel declared in profile.Channels)
        {
            string mark = taken.Contains(declared.Id) ? "[mapped]" : "[      ]";
            draft.AppendLine(
                $"//   {mark} {declared.Id} ({Show(declared.Unit)}, "
                + $"{Number(declared.Minimum)}..{Number(declared.Maximum)})");
        }
    }

    /// <summary>How one candidate reads in the drafted file's comments.</summary>
    private static string Describe(ChannelCandidate candidate)
    {
        string scale = candidate.Gain == 1.0
            ? string.Empty
            : $", and \"gain\": {UnitScale.Format(candidate.Gain)}";

        return $"{candidate.Declared.Id} ({Show(candidate.Declared.Unit)}, "
             + $"{Number(candidate.Declared.Minimum)}..{Number(candidate.Declared.Maximum)}{scale})";
    }

    /// <summary>The comment lines that go above an unmapped channel in the drafted file.</summary>
    private static IEnumerable<string> CandidateLines(IReadOnlyList<ChannelCandidate> fits, bool hasProfile)
    {
        if (!hasProfile)
        {
            yield return "        //   no profile was named, so there is nothing to map onto yet.";
            yield break;
        }

        if (fits.Count == 0)
        {
            yield return
                "        //   nothing this profile declares has a unit and a range these readings "
                + "would fit, so the line below is left blank.";
            yield break;
        }

        yield return fits.Count == 1
            ? $"        //   one declared channel fits: {Describe(fits[0])}"
            : $"        //   closest of {fits.Count} by unit and range: {Describe(fits[0])}";

        foreach (ChannelCandidate other in fits.Skip(1))
        {
            yield return $"        //   also possible: {Describe(other)}";
        }

        yield return IsDecisive(fits)
            ? "        //   delete the // below to accept it, or change the name first."
            : "        //   none of these fits closely enough to be written out for you: the band"
              + " is wider than the reading tells apart. Fill in the one you know it is.";
    }

    private static string Show(string unit) => unit.Length > 0 ? unit : "no unit";

    private static string Number(double value) =>
        value.ToString("G6", CultureInfo.InvariantCulture);
}
