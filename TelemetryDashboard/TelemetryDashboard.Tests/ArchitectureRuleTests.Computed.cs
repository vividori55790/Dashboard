using System;
using System.IO;
using System.Text.RegularExpressions;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Keeps the console's derived-value panel and the computed endpoint from drifting apart.
/// </summary>
/// <remarks>
/// Efficiency is the number a converter is judged by and no converter reports it; neither side's
/// power is reported either, because each is a voltage and a current from separate MCUs arriving at
/// separate instants. The host computes them and publishes the results into the stream, so the
/// channel cards show them when they exist.
/// <para>
/// That is exactly the gap this panel fills. When an input goes quiet the computed channel simply
/// stops arriving, and a card that is not there looks like a channel nobody declared rather than a
/// quantity nobody can work out. The declaration list makes a missing answer visible as a missing
/// answer, with the host naming the input that was not there.
/// </para>
/// <para>
/// Driven live in a browser against two running hosts. Simulated: 선언 5 · 지금 값 있음 5, with
/// psfb.p_out[W] reading 9852 W. On an SSE source where only one channel was mapped: 0 of 5, and
/// each row named its own failure — "psfb.output_current: 해석되는 채널이 없습니다" against
/// "psfb.output_voltage: 이 순간에 값이 없습니다", which are different faults with different fixes.
/// </para>
/// </remarks>
public partial class ArchitectureRuleTests
{
    /// <summary>The endpoint and the record shapes beside it, which is where the vocabulary is.</summary>
    private static string ComputedEndpointSource() =>
        File.ReadAllText(Path.Combine(
            SolutionRoot, "TelemetryDashboard.Core", "Streaming", "ComputedEndpoint.cs"))
        + File.ReadAllText(Path.Combine(
            SolutionRoot, "TelemetryDashboard.Core", "Streaming", "ComputedEndpointModels.cs"));

    [Fact]
    [Trait("Category", "Architecture")]
    public void ThePanelKnowsBothStatesAComputedChannelCanBeIn()
    {
        // Computed or Unavailable, and the panel branches on the first. A third state would fall
        // into the "값 없음" arm and be shown as a failure, which is the wrong way round for a
        // status that might mean something else.
        string endpoint = ComputedEndpointSource();
        string page = ConsolePage();

        endpoint.Should().Contain("<c>Computed</c> or <c>Unavailable</c>",
            "the endpoint documents its own vocabulary");
        page.Should().Contain("row.Status === 'Computed'");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void AMissingAnswerNamesTheInputRatherThanJustSayingNothing()
    {
        // The endpoint reports per-input detail precisely so a caller does not have to guess which
        // half of an expression failed. A panel that showed only "no value" would throw that away.
        string page = ConsolePage();

        page.Should().Contain("row.Inputs");
        page.Should().Contain("AnswersTheInstant",
            "the flag that separates an input that resolved from one that answered");
        page.Should().Contain("i.Resolved",
            "and the field that separates a name nothing reports from a sensor that went quiet");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void TheRowNamesTheQuantityAndNotOnlyItsArithmetic()
    {
        // An expression says how a number is made and not what it is. Shown on its own, a row read
        // "dab.bus_voltage * dab.input_current" and never said the word an operator is looking for.
        string page = ConsolePage();

        page.Should().MatchRegex(@"esc\(row\.Id \+ \(row\.Unit",
            "the identity leads, the expression follows");
    }
}
