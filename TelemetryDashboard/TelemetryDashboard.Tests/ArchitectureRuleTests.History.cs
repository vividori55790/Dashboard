using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Keeps the console's archive panel and the archive endpoint from drifting apart.
/// </summary>
/// <remarks>
/// The host keeps an archive, prunes it, tiers it and exports it, and until this panel existed
/// nothing this product ships could show a byte of it: an operator watching from a phone saw what
/// was arriving at that moment and nothing that had already happened. It was reachable by curl.
/// <para>
/// A page and an endpoint that disagree fail quietly here — a renamed query parameter is simply
/// ignored by the server, which answers with its default window, and a renamed response field
/// reads as undefined and renders as nothing. Both look like an empty archive.
/// </para>
/// </remarks>
public partial class ArchitectureRuleTests
{
    private static string ConsolePage() =>
        File.ReadAllText(Path.Combine(Directory.GetParent(SolutionRoot)!.FullName, "stream_client.html"));

    private static string HistoryEndpointSource() =>
        File.ReadAllText(Path.Combine(
            SolutionRoot, "TelemetryDashboard.Core", "Streaming", "HistoryEndpoint.cs"));

    private static string HistoryRouteSource() =>
        File.ReadAllText(Path.Combine(
            SolutionRoot, "TelemetryDashboard.Core", "Streaming", "TelemetryHttpRoutes.cs"));

    [Fact]
    [Trait("Category", "Architecture")]
    public void EveryQueryParameterTheConsoleSendsToTheArchiveIsRead()
    {
        // A parameter the route does not read is not an error: the endpoint answers with its
        // default window, so asking for six hours quietly returns the default and the chart looks
        // like a rig that only just started.
        string page = ConsolePage();
        int at = page.IndexOf("/api/history?", StringComparison.Ordinal);
        at.Should().BeGreaterThan(0, "the console reads the archive");

        string request = page[at..(at + 400)];
        string[] sent = Regex.Matches(request, @"[?&]([a-zA-Z]+)=")
            .Select(m => m.Groups[1].Value).Distinct().ToArray();

        sent.Should().Contain(new[] { "node", "channel", "from", "to", "limit" });

        string route = HistoryRouteSource();
        foreach (string parameter in sent)
        {
            route.Should().Contain($"QueryString[\"{parameter}\"]",
                $"the console sends ?{parameter}= and the route has to read it");
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void EverySampleFieldTheConsoleReadsIsOneTheArchiveSends()
    {
        string endpoint = HistoryEndpointSource();
        var known = new HashSet<string>(
            Regex.Matches(endpoint, @"public\s+[\w\.<>\?\[\]]+\s+(\w+)\s*\{\s*get")
                .Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);

        // The Sample record's positional members, which have no get accessor to match on.
        foreach (System.Text.RegularExpressions.Match member in
                 Regex.Matches(endpoint, @"record Sample\(([^)]*)\)"))
        {
            foreach (string part in member.Groups[1].Value.Split(','))
            {
                known.Add(part.Trim().Split(' ').Last());
            }
        }

        string[] read = Regex.Matches(ConsolePage(), @"\bs\.([A-Z]\w+)")
            .Select(m => m.Groups[1].Value).Distinct().ToArray();

        read.Should().NotBeEmpty("the panel plots archived samples");
        read.Except(known).Should().BeEmpty("a field the archive does not send renders as nothing");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void TheConsoleSaysWhenTheArchiveTruncatedTheAnswerAndWhyItRefused()
    {
        // Both are honesty properties rather than features, and both are one simplification away
        // from being dropped. A chart drawn from a truncated read shows the beginning of the
        // window while looking like the whole of it; a refusal shown as "no data" reads as a quiet
        // rig rather than as a hub keeping no record.
        string page = ConsolePage();

        page.Should().Contain("d.Truncated", "a partial read must not look like a complete one");
        page.Should().Contain("d.Reason", "the host explains why it cannot answer; show what it said");
    }
}
