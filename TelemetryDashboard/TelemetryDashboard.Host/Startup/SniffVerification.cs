using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Whether the rules now in force actually feed the channels the profile declares.
/// </summary>
/// <remarks>
/// The drafting half of <c>sniff</c> was complete and the confirming half was not, in two specific
/// ways that only show up once somebody uses the command the way its own help suggests --
/// <c>--rules</c> against a file they have already edited.
/// <para>
/// It wrote a draft anyway. Verifying a file meant producing a second one, and refusing outright if
/// <c>rules.json</c> was already there, so the check had a side effect and a guard against its own
/// side effect. And it exited 0 whenever any line arrived, which is the wrong question: a run where
/// every declared channel is silent is exactly the run an operator needs to fail, because it is
/// indistinguishable on a chart from a rig that is merely quiet.
/// </para>
/// <para>
/// What it does <em>not</em> add is the report. <c>SniffReport.AgainstProfile</c> already answered
/// this correctly, because <see cref="RuleDraft.MapByName"/> matches an exact declared id first and
/// the router has already renamed the readings by the time the survey sees them. That was checked
/// before anything was written here, and finding it already done is the reason this file is small.
/// </para>
/// </remarks>
public static class SniffVerification
{
    /// <summary>The declared channels that received readings, and the ones that did not.</summary>
    /// <remarks>
    /// Reuses <see cref="RuleDraft.MapByName"/> rather than comparing ids here. Two definitions of
    /// "this reading is that channel" would be free to disagree, and the one an operator would
    /// meet first is whichever printed last.
    /// </remarks>
    public static (string[] Fed, string[] Silent) Coverage(WireSurvey survey, MonitoringProfile profile)
    {
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(profile);

        var fed = new HashSet<string>(
            RuleDraft.MapByName(survey.Channels, profile).Values, StringComparer.OrdinalIgnoreCase);

        return (
            profile.Channels.Where(c => fed.Contains(c.Id)).Select(c => c.Id).ToArray(),
            profile.Channels.Where(c => !fed.Contains(c.Id)).Select(c => c.Id).ToArray());
    }

    /// <summary>
    /// The verdict lines, built so their wording can be asserted without a console or a device.
    /// </summary>
    public static string[] Render(WireSurvey survey, MonitoringProfile? profile, int ruleCount)
    {
        ArgumentNullException.ThrowIfNull(survey);

        // A guard, not a path an operator reaches. HostFeatureSetup.ActiveProfile falls back to
        // generic-machine when --profile is absent, so the CLI cannot produce this state; the
        // parameter is nullable and dropping the branch would only turn null into a
        // NullReferenceException further down. Worded as what it is, because a message telling
        // somebody to pass --profile when passing it was never the problem sends them looking in
        // the wrong place.
        if (profile is null)
        {
            return
            [
                "Nothing was verified: there is no profile to check these readings against.",
                "A verification with no claim to check is not a passing verification."
            ];
        }

        (string[] fed, string[] silent) = Coverage(survey, profile);

        var lines = new List<string>
        {
            string.Empty,
            $"Verifying profile '{profile.Id}' against {Describe(ruleCount)}.",
            $"  fed       {fed.Length} of {profile.Channels.Count} declared channel(s)"
        };

        if (silent.Length == 0)
        {
            lines.Add("  silent    none -- every declared channel received readings.");
            return [.. lines];
        }

        lines.Add($"  silent    {string.Join(", ", silent)}");
        lines.Add(string.Empty);
        lines.Add(
            "  A silent channel is not a quiet machine. Every band, computed channel and twin");
        lines.Add(
            "  placement naming one of these matches nothing, so the alarm that would have fired");
        lines.Add(
            "  cannot, and the screen looks the same either way.");

        return [.. lines];
    }

    /// <summary>
    /// The exit code, which is the half a script can act on.
    /// </summary>
    /// <remarks>
    /// Non-zero while any declared channel is silent, so <c>sniff --verify</c> can gate a
    /// commissioning step rather than being read by whoever happens to be watching. Also non-zero
    /// when nothing arrived at all, and for the same reason the drafting path already does that:
    /// a run that heard nothing must not report success in the same words as one that worked.
    /// </remarks>
    public static int ExitCode(WireSurvey survey, MonitoringProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(survey);

        if (survey.Lines == 0) return 1;
        if (profile is null) return 1;

        return Coverage(survey, profile).Silent.Length == 0 ? 0 : 1;
    }

    private static string Describe(int ruleCount) => ruleCount switch
    {
        0 => "no rules at all -- the readings are being judged under the built-in framing",
        1 => "1 rule in force",
        _ => $"{ruleCount} rules in force"
    };
}
