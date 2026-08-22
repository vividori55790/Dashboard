using FluentAssertions;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.UI.ViewModels;
using Xunit;

namespace TelemetryDashboard.Tests.Desktop;

/// <summary>
/// The two pieces the scope needed before its cursors could be drawn and read.
/// </summary>
/// <remarks>
/// <see cref="DeltaCursorService"/> shipped complete and was constructed by nothing but its own
/// tests, so the scope could show a transient and offered no way to say how long it lasted. Wiring
/// it needed one addition to the service and one new readout, and both are here.
/// </remarks>
public class ScopeCursorWiringTests
{
    // ---- knowing that one cursor is down --------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void OneCursorPlacedIsSomethingToDrawButNotSomethingToMeasure()
    {
        // The distinction the drawing code needs. Without it, "placed" has to be inferred from the
        // coordinates -- and a cursor legitimately placed at the origin is then indistinguishable
        // from one that was never placed at all.
        var cursors = new DeltaCursorService();

        cursors.HasAnyCursor.Should().BeFalse();
        cursors.HasValidMeasurement.Should().BeFalse();

        cursors.SetCursor1(1.5, 48.2);

        cursors.HasAnyCursor.Should().BeTrue("there is a cursor on the plot to draw");
        cursors.HasValidMeasurement.Should().BeFalse("one cursor is a half-finished gesture");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ACursorPlacedAtTheOriginStillCounts()
    {
        var cursors = new DeltaCursorService();

        cursors.SetCursor1(0.0, 0.0);

        cursors.HasAnyCursor.Should().BeTrue(
            "zero is a coordinate like any other -- this is the case a coordinate check gets wrong");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ClearingTheTraceTakesTheCursorsWithIt()
    {
        // Wired to the scope's Clear button. A measurement left on screen after the samples it was
        // taken from have gone is a reading of nothing.
        var cursors = new DeltaCursorService();
        cursors.SetCursor1(1.0, 10.0);
        cursors.SetCursor2(2.0, 20.0);
        cursors.HasValidMeasurement.Should().BeTrue();

        cursors.ClearData();

        cursors.HasAnyCursor.Should().BeFalse();
        cursors.HasValidMeasurement.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheDeltaKeepsItsSignSoALeadReadsDifferentlyFromALag()
    {
        var cursors = new DeltaCursorService();
        cursors.SetCursor1(3.0, 50.0);
        cursors.SetCursor2(1.0, 20.0);

        cursors.DeltaTime.Should().Be(-2.0);
        cursors.DeltaValue.Should().Be(-30.0);
        cursors.AbsoluteDeltaTime.Should().Be(2.0);
    }

    // ---- the readout ----------------------------------------------------------

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData(0.0000123, "12.30 µs")]
    [InlineData(0.000999, "999.00 µs")]
    [InlineData(0.0012, "1.200 ms")]
    [InlineData(0.717457, "717.457 ms")]
    [InlineData(4.4841, "4.4841 s")]
    [InlineData(-3.5873, "-3.5873 s")]
    public void AnIntervalIsWrittenInTheUnitThatReadsWithoutCountingZeros(double seconds, string expected)
    {
        // A converter's interesting intervals span six orders of magnitude: a switching period is
        // microseconds and a soft-start is seconds. "0.0000123 s" is a number an operator has to
        // count digits to read, and reading it wrong by a factor of ten is the whole risk.
        IntervalFormat.Seconds(seconds).Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheUnitBoundariesDoNotOverlapOrLeaveAGap()
    {
        // Exactly one millisecond and exactly one second are the two seams. Both belong to the
        // larger unit, so nothing is ever written as "1000.00 us".
        IntervalFormat.Seconds(0.001).Should().Be("1.000 ms");
        IntervalFormat.Seconds(1.0).Should().Be("1.0000 s");
        IntervalFormat.Seconds(0.0).Should().Be("0.00 µs");
    }
}
