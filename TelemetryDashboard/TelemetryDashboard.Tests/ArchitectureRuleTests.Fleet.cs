namespace TelemetryDashboard.Tests;

/// <summary>
/// Keeps the console's fleet panel honest about the three states it can be in.
/// </summary>
/// <remarks>
/// Everything §3 and §4 measure — whether this console is exposed, how far each peer's clock is
/// from this one and how well that is known, whether a replayed link has been inflating the totals
/// — existed only as JSON until this panel. An operator cannot act on a field nobody renders.
/// <para>
/// A fleet view is also the surface where this project's central rule breaks most easily, because
/// an empty table reads as a healthy one. Every block has to draw the distinction the payload
/// already draws: null is "nobody is measuring", empty is "measuring, and nothing has arrived", and
/// only populated means what an operator would assume all three mean.
/// </para>
/// <para>
/// Driven in a browser against four live hosts rather than a DOM stub. A host with a replaying peer
/// showed <c>중복 210개 거부 · 수용 30개</c> and <c>PEER-01 ±0.14 ms</c>; a host with no network
/// ingest showed "measuring, but nothing has carried a clock — not an offset of zero"; a peer that
/// stamps no sequence showed "not checked — a zero duplicate count does not mean clean". Nothing
/// overflowed its panel and the console logged no errors.
/// </para>
/// </remarks>
public partial class ArchitectureRuleTests
{
    private static string StatusPayloadSource() =>
        File.ReadAllText(Path.Combine(
            SolutionRoot, "TelemetryDashboard.Core", "Streaming", "TelemetryHttpRoutes.cs"));

    [Fact]
    [Trait("Category", "Architecture")]
    public void ThePanelReadsTheFleetFieldsTheStatusPayloadActuallySends()
    {
        // Read off the payload rather than listed here, so a rename fails this instead of quietly
        // moving the goalposts. The endpoint builds an anonymous object, which makes a rename a
        // compile-time nothing and a run-time panel showing a blank fleet.
        string payload = StatusPayloadSource();
        string page = StreamClientSource();

        string[] fields =
        [
            "reachability", "scope", "authenticated", "encrypted",
            "clocks", "perNode", "offsetSec", "spreadSec", "samples",
            "exchange", "admitted", "duplicatesRefused", "unsequenced", "senderEvictions",
            "link", "outages", "totalDownSec", "recent"
        ];

        string[] missingFromPayload = fields.Where(f => !payload.Contains(f + " =")).ToArray();
        missingFromPayload.Should().BeEmpty(
            "this list mirrors the payload; a field named here and not there is this test drifting "
            + "rather than the page:\n" + string.Join(", ", missingFromPayload));

        string[] unread = fields.Where(f => !page.Contains(f)).ToArray();
        unread.Should().BeEmpty(
            "a fleet field the status endpoint sends and the panel never names is either dead "
            + "weight on the wire or something the operator was supposed to see:\n"
            + string.Join(", ", unread));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void TheClockPanelSeparatesNotMeasuringFromMeasuringNothing()
    {
        // The distinction the whole product is organised around, at the scale where it is easiest
        // to lose. An empty table drawn for both tells an operator the fleet's clocks agree when
        // nothing ever compared them.
        string page = StreamClientSource();

        page.Should().Contain("잰 적이 없다",
            "the ledger-attached-but-nothing-heard case has to say so in words, because the honest "
            + "rendering of it looks exactly like a clean fleet");
        page.Should().Contain("시계를 비교하지 않습니다",
            "and the no-ledger case is a third thing again");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void TheSpreadIsNeverShownWithoutSayingItIsAFloor()
    {
        // One-way messages never separate transit from the offset, so the published spread is a
        // lower bound. A panel showing it bare invites an operator to order two events that cannot
        // be ordered -- which is precisely what §3 exists to prevent.
        string page = StreamClientSource();

        page.Should().Contain("하한", "the caveat has to travel with the number, not sit in a doc");
        page.Should().Contain("오차 막대 없음",
            "and a single-observation offset has to be shown as having no error bar at all, rather "
            + "than as a tight one");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void AZeroDuplicateCountIsNeverShownAsACleanBillOfHealth()
    {
        // The one number here that must not be read alone. A link whose sender stamps no sequence
        // reports zero duplicates forever, and zero there means "nothing was watching".
        string page = StreamClientSource();

        page.Should().Contain("unsequenced",
            "the panel has to branch on it, not merely receive it");
        page.Should().Contain("깨끗하다는 뜻이 아닙니다",
            "and say plainly what the zero does not mean");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void HavingNoUpstreamIsNotShownAsAnUpstreamThatNeverFailed()
    {
        // Two good-looking states with opposite meanings. "No link to lose" and "a link that has
        // never dropped" would both render as an absence of bad news, and only the second is
        // actually news. Same shape as the clocks block above, one layer out.
        string page = StreamClientSource();

        page.Should().Contain("상위 링크가 없습니다");
        page.Should().Contain("끊긴 적 없음");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void AnOutageIsShownAsAnIntervalRatherThanACount()
    {
        // Four reconnections in a minute and one four-hour gap give the same counter. A panel
        // showing only the counter hides the one that put a hole in the chart, which is the exact
        // failure SseTelemetrySource's own summary describes and then wrote to stderr.
        string page = StreamClientSource();

        page.Should().Contain("totalDownSec", "the duration has to be read, not just the count");
        page.Should().Contain("가장 긴 끊김", "and the worst single gap is the one that matters");
        page.Should().Contain("조용한 설비로 읽지 마십시오",
            "and the panel has to say what the gap means for the chart beside it -- an operator "
            + "reading a flat stretch as a calm plant is the failure this is all for");
    }
}
