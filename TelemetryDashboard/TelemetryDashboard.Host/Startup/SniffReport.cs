using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// What was heard, said on the console before anyone opens the file.
/// </summary>
/// <remarks>
/// The file is the deliverable; this is the answer to the question the operator actually has, which
/// is "is my device even talking to this thing". It is printed whether or not anything arrived,
/// because the informative cases are the empty ones: no lines at all is a cable, lines under a tag
/// nothing claims is a firmware format, and readings under names the profile does not declare is
/// the ordinary case this command exists to fix.
/// </remarks>
internal static class SniffReport
{
    public static void Print(WireSurvey survey, MonitoringProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(survey);

        IReadOnlyList<WireChannel> channels = survey.Channels;
        Console.WriteLine();
        Console.WriteLine($"Heard {survey.Lines:N0} line(s); {channels.Count} channel(s) arrived.");

        if (survey.Lines == 0)
        {
            Console.WriteLine(
                "  Nothing arrived at all. That is the device, the cable or the baud rate rather "
                + "than the mapping — a wrong frame format still produces lines.");
            return;
        }

        foreach (WireChannel channel in channels)
        {
            string unit = channel.Unit.Length > 0 ? channel.Unit : "(no unit)";
            Console.WriteLine(
                $"  ${channel.Tag,-6} {channel.NodeId,-10} {channel.Name,-16} {unit,-6} "
                + $"{channel.Samples,7:N0}  {channel.Range}");
        }

        Unclaimed(survey);
        AgainstProfile(channels, profile);
    }

    private static void Unclaimed(WireSurvey survey)
    {
        if (survey.UnreadableLines == 0) return;

        Console.WriteLine();
        Console.WriteLine($"  {survey.UnreadableLines:N0} line(s) no rule claimed.");

        foreach ((string tag, long count) in survey.UnclaimedTags.OrderByDescending(t => t.Value))
        {
            Console.WriteLine(
                $"    ${tag}: {count:N0} line(s). The drafted file declares a rule for it, which is "
                + "what makes those lines readable.");
        }

        if (survey.UnclaimedTags.Count == 0)
        {
            Console.WriteLine(
                "    None of them began with a $TAG. If the device speaks JSON, it is --map that "
                + "describes it rather than --rules.");
        }
    }

    private static void AgainstProfile(IReadOnlyList<WireChannel> channels, MonitoringProfile? profile)
    {
        if (profile is null)
        {
            Console.WriteLine();
            Console.WriteLine(
                "  No profile was named, so nothing here was checked against one. Add "
                + "--profile <id> and the draft will say which declared channel each reading fits.");
            return;
        }

        IReadOnlyDictionary<string, string> mapped = RuleDraft.MapByName(channels, profile);
        var taken = new HashSet<string>(mapped.Values, StringComparer.OrdinalIgnoreCase);
        string[] silent = profile.Channels
            .Where(c => !taken.Contains(c.Id))
            .Select(c => c.Id)
            .ToArray();

        Console.WriteLine();
        Console.WriteLine(
            $"  Profile '{profile.Id}' declares {profile.Channels.Count} channel(s); "
            + $"{mapped.Count} arrived under a name it recognises.");

        if (silent.Length == 0) return;

        Console.WriteLine($"  Nothing maps onto: {string.Join(", ", silent)}");
        Console.WriteLine(
            "  Until they do, every band, computed channel and twin placement naming them matches "
            + "nothing — the readings chart themselves and no one judges them.");
    }
}
