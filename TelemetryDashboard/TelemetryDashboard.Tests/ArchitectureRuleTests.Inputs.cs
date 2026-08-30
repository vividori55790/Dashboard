using System;
using System.IO;
using System.Linq;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Keeps the console's input panel and <c>/api/inputs</c> from drifting apart.
/// </summary>
/// <remarks>
/// Every other panel on that page is organised by channel, which only somebody who already knows
/// the channel names can read — and a rig being commissioned is exactly the case where nobody does.
/// This one is grouped by the thing an operator can unplug, and its checkboxes decide which cards
/// the home grid shows.
/// <para>
/// The names are the fragile part. The endpoint builds an anonymous object, so a rename there is a
/// compile-time nothing and a run-time panel that quietly shows an empty list — the same failure the
/// wire-contract rule was written for after a console spent 214 frames discarding every one.
/// </para>
/// <para>
/// Driven live in a browser against a running host rather than a DOM stub: 포트 1 · 입력 10, ten
/// checkboxes, rows reading "dab.bus_voltage[V] · 지금 · 401.0 · 9.5 Hz · 47개". Unchecking hid the
/// matching card and a page reload kept it hidden — 13 of 15 cards visible, the two chosen ones
/// still <c>display:none</c>. No console errors and nothing positioned outside the panel.
/// </para>
/// </remarks>
public partial class ArchitectureRuleTests
{
    private static string InputsEndpointSource() =>
        File.ReadAllText(Path.Combine(
            SolutionRoot, "TelemetryDashboard.Core", "Streaming", "InputsEndpoint.cs"));

    private static string InputsClassificationSource() =>
        File.ReadAllText(Path.Combine(
            SolutionRoot, "TelemetryDashboard.Core", "Streaming", "InputsEndpoint.Classification.cs"));

    private static string StreamClientSource() =>
        File.ReadAllText(Path.Combine(
            Directory.GetParent(SolutionRoot)?.FullName ?? SolutionRoot, "stream_client.html"));

    [Fact]
    [Trait("Category", "Architecture")]
    public void ThePanelReadsTheFieldsTheEndpointActuallySends()
    {
        // Read off the endpoint rather than listed here, so a rename fails this instead of silently
        // moving the goalposts.
        string endpoint = InputsEndpointSource();
        string page = StreamClientSource();

        string[] fields = ["tracking", "distinctInputs", "evicted", "ports", "channels",
                           "node", "channel", "unit", "lastValue", "samples",
                           "silenceSec", "meanIntervalSec"];

        string[] missingFromEndpoint = fields.Where(f => !endpoint.Contains(f + " =")).ToArray();
        missingFromEndpoint.Should().BeEmpty(
            "this list is meant to mirror the endpoint; a field named here and not there is this "
            + "test drifting rather than the page:\n" + string.Join(", ", missingFromEndpoint));

        string[] unread = fields.Where(f => !page.Contains(f)).ToArray();
        unread.Should().BeEmpty(
            "a field the endpoint sends and the panel never names is either dead weight on the wire "
            + "or a column the operator was supposed to get:\n" + string.Join(", ", unread));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void ThePanelDistinguishesNobodyLookingFromNothingArriving()
    {
        // The distinction the whole product is organised around, at its smallest scale. An empty
        // table drawn for both tells an operator their rig is silent when the truth is that nothing
        // was ever asked.
        string page = StreamClientSource();

        page.Should().Contain("d.tracking",
            "the panel has to branch on it, not merely receive it");
        page.Should().Contain("집계 안 함",
            "and say which of the two states it is in");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void HidingAChannelIsSaidToBeAViewChoiceRatherThanAnIngestOne()
    {
        // A control that quietly stopped recording a channel would be the "silence looks like
        // health" failure this product exists to prevent, dressed as a convenience. The panel says
        // what unchecking does, and the checkbox handler must only touch visibility.
        string page = StreamClientSource();

        page.Should().Contain("수신·기록·판정은 그대로입니다",
            "the operator has to be told that hiding is not muting");

        page.Should().Contain("style.display",
            "and hiding must be exactly that -- a display change on the card");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void TheWireNeverCarriesAQuantityKindWithoutTheFieldsThatQualifyIt()
    {
        // ROADMAP W1's rule, at the boundary where it is easiest to lose. Downstream this is the
        // field that picks an axis, a scale and an alarm band, and a consumer reading it alone
        // cannot tell a derivation from a guess. Enforced against the source rather than only in
        // behaviour because these are anonymous-object member names: deleting one is a compile-time
        // nothing and a run-time consumer that silently stops seeing the qualification.
        string classification = InputsClassificationSource();

        classification.Should().Contain("kind = ", "the endpoint has to publish one at all");

        foreach (string qualifier in new[] { "confidence = ", "proposal = ", "disputed = ", "why = ", "evidence = " })
        {
            classification.Should().Contain(qualifier,
                $"a kind published without {qualifier.Trim(' ', '=')} reads as a fact whatever it was reached from");
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void TheHiddenSetIsStoredRatherThanTheShownSet()
    {
        // Storing what to show would mean a channel added to the rig tomorrow is absent from a list
        // written today, and so invisible for a reason nobody can see. Storing what to hide means
        // anything new appears.
        string page = StreamClientSource();

        page.Should().Contain("td.hiddenChannels");
        page.Should().NotContain("td.shownChannels");
    }
}
