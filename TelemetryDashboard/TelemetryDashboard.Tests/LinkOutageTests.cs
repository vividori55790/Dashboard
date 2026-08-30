using TelemetryDashboard.Core.Cluster;

namespace TelemetryDashboard.Tests;

/// <summary>
/// ARCHITECTURE §4 is titled "A node must survive alone, <em>and say so when it was</em>".
/// </summary>
/// <remarks>
/// Only the first half was built. <c>SseTelemetrySource</c> reconnects and counts reconnections —
/// its own summary says why: "A feed that drops every thirty seconds and silently resumes looks
/// identical to a healthy one from the chart, and the gaps it leaves are exactly the intervals an
/// operator would otherwise read as quiet." It then wrote that to stderr, where a browser cannot
/// see it and a service manager loses it.
/// <para>
/// A count is also not the fact. Four reconnections in a minute and one four-hour outage give the
/// same counter and are not the same situation, and only the second puts a hole in a chart.
/// </para>
/// <para>
/// Driven against a live host reading a peer whose connection ends after every batch: 26 outages,
/// 78.271 s total, longest 3.025 s — the reconnect delay, as expected. A host reading its own port
/// reports null, and a host on a stable upstream reports zero outages, which is a different and
/// much better fact than having no upstream at all.
/// </para>
/// </remarks>
public class LinkOutageTests
{
    private static readonly DateTime T0 = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnOutageIsKeptAsAnIntervalRatherThanATally()
    {
        var ledger = new LinkOutageLedger();

        ledger.Dropped(T0, "IOException: connection reset");
        ledger.Restored(T0.AddHours(4));

        ledger.Count.Should().Be(1);
        ledger.Total.Should().Be(TimeSpan.FromHours(4),
            "'reconnected once' and 'was gone for four hours' are the same counter and different "
            + "situations, and only one of them is a hole in a chart");
        ledger.IsDown.Should().BeFalse();

        LinkOutage gap = ledger.Recent().Single();
        gap.BeganUtc.Should().Be(T0);
        gap.EndedUtc.Should().Be(T0.AddHours(4));
        gap.Open.Should().BeFalse();
        gap.Fault.Should().Contain("connection reset");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnOutageStillOpenIsReportedAsOpenRatherThanOmitted()
    {
        // The interval an operator most needs. Waiting for it to close before reporting it means
        // the one outage that is still costing data is the one nothing mentions.
        var ledger = new LinkOutageLedger();
        ledger.Dropped(T0, null);

        ledger.IsDown.Should().BeTrue();
        LinkOutage open = ledger.Recent().Single();
        open.Open.Should().BeTrue();
        open.Duration(T0.AddMinutes(7)).Should().Be(TimeSpan.FromMinutes(7),
            "an open interval is measured against now, not against a null end");
        ledger.Total.Should().Be(TimeSpan.Zero, "the total is of closed intervals, and says so");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ReportingAFaultAndThenTheStreamEndingIsOneOutageNotTwo()
    {
        // A source can notice the same failure twice -- an exception, then the enumerator finishing.
        // Opening a second interval for it would double the total and invent an outage.
        var ledger = new LinkOutageLedger();

        ledger.Dropped(T0, "HttpRequestException");
        ledger.Dropped(T0.AddSeconds(1), "stream ended");
        ledger.Restored(T0.AddSeconds(10));

        ledger.Count.Should().Be(1);
        ledger.Total.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ARestoreWithNothingOpenIsIgnored()
    {
        var ledger = new LinkOutageLedger();
        ledger.Restored(T0);

        ledger.Count.Should().Be(0);
        ledger.Recent().Should().BeEmpty();
        ledger.Total.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AFlappingLinkKeepsItsTotalsWhileTheIntervalsRollOff()
    {
        // Bounded memory, unbounded counting. A link that dropped a thousand times still reports a
        // thousand; what is dropped is the middle of the list, not the fact that it happened.
        var ledger = new LinkOutageLedger();

        for (int i = 0; i < LinkOutageLedger.Kept * 3; i++)
        {
            ledger.Dropped(T0.AddSeconds(i * 10), null);
            ledger.Restored(T0.AddSeconds(i * 10 + 2));
        }

        ledger.Count.Should().Be(LinkOutageLedger.Kept * 3);
        ledger.Total.Should().Be(TimeSpan.FromSeconds(2 * LinkOutageLedger.Kept * 3));
        ledger.Recent().Should().HaveCount(LinkOutageLedger.Kept);
    }
}
