using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Keeps the console's safety-band panel and the limits endpoint from drifting apart.
/// </summary>
/// <remarks>
/// The endpoint answered this question from the day it was written and nothing this product ships
/// asked it. An operator on a phone saw channel cards reading "정상" and had no way to learn that
/// every band declared for the rig was matching nothing — which is what a mistyped channel name,
/// or a device sending millivolts against a band in volts, looks like from there.
/// <para>
/// Both halves fail silently when they drift. A renamed response field reads as <c>undefined</c>
/// in the page and renders as an empty cell; a new status value falls into the panel's final
/// branch and is displayed as "never evaluated", which is a different and more alarming claim than
/// whatever the endpoint meant. So the field names and the status vocabulary are pinned here.
/// </para>
/// <para>
/// Driven live against a running host on an SSE source: unmapped, the panel read "선언 7 · 위반 0 ·
/// 무장 안 됨 7" with every band "평가된 적 없음"; with the mapping applied, one band read "감시 중 ·
/// 평가 354회 · 마지막 47.8 V"; with a band the readings breach, "위반 · 위반 228회 · 47.7522 is
/// below the 48.5 floor".
/// </para>
/// </remarks>
public partial class ArchitectureRuleTests
{
    private static string LimitsEndpointSource() =>
        File.ReadAllText(Path.Combine(
            SolutionRoot, "TelemetryDashboard.Core", "Streaming", "LimitsEndpoint.cs"));

    [Fact]
    [Trait("Category", "Architecture")]
    public void EveryFieldTheSafetyBandPanelReadsIsOneTheEndpointSends()
    {
        // A field the endpoint does not send is undefined in JavaScript, and undefined renders as
        // an empty cell rather than as an error. The panel would go on looking like a working
        // panel with nothing in it.
        string page = ConsolePage();
        string endpoint = LimitsEndpointSource();

        page.Should().Contain("/api/limits", "the console has to ask the endpoint");

        string[] fields =
        [
            "Status", "Reason", "Declared", "Breached", "Unarmed", "Rules",
            "Declaration", "InBreach", "Evaluated", "Breaches", "LastValue", "LastSeenUtc"
        ];

        var missing = new List<string>();
        foreach (string field in fields)
        {
            if (!Regex.IsMatch(page, @"\brow\." + field + @"\b|\bd\." + field + @"\b")) continue;
            if (!endpoint.Contains(" " + field + " ", StringComparison.Ordinal)) missing.Add(field);
        }

        missing.Should().BeEmpty("the panel reads fields the endpoint no longer declares");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void ThePanelKnowsEveryStatusTheEndpointCanReport()
    {
        // The failure this catches is the quiet one. A status the panel does not recognise falls
        // into its last branch and is shown as "never evaluated" -- a claim that the rig is
        // unprotected, made about a rule that may be doing its job.
        string endpoint = LimitsEndpointSource();
        string page = ConsolePage();

        // The ternary that decides it, read from where it is written rather than from a list kept
        // here -- a list would agree with itself forever.
        System.Text.RegularExpressions.Match chain = Regex.Match(endpoint, @"Status = state\.[\s\S]*?""Watching""");
        chain.Success.Should().BeTrue("the endpoint decides a status somewhere");

        string[] produced = Regex.Matches(chain.Value, "\"([A-Za-z]+)\"")
            .Select(v => v.Groups[1].Value)
            .Distinct()
            .ToArray();

        produced.Should().BeEquivalentTo(
            new[] { "Unarmed", "Never", "Breached", "Watching" },
            "these are the four states a declared limit can be in");

        // Breached is read through InBreach rather than by name, which is the same fact arriving
        // by the field that also drives the row's colour.
        page.Should().Contain("row.InBreach");
        page.Should().Contain("'Watching'");
        page.Should().Contain("'Unarmed'");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void TheUnarmedCountIsSaidInWordsAndNotOnlyCounted()
    {
        // The number on its own is the one an operator will read as good news. A host with seven
        // declared limits and seven of them unarmed is not a calm plant, and the panel has to say
        // which of the two silences it is looking at.
        string page = ConsolePage();
        int at = page.IndexOf("lim-stat", StringComparison.Ordinal);

        at.Should().BeGreaterThan(0);
        page.Should().Contain("d.Unarmed > 0",
            "the panel has to branch on it rather than print it beside the others");
    }
}
