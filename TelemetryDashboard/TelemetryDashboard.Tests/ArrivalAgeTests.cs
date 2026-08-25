using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Tests;

/// <summary>
/// ARCHITECTURE §4: a sample that took four hours to arrive is a different fact from one that
/// arrived instantly.
/// </summary>
/// <remarks>
/// The first consumer of <see cref="ClockOffsetEstimate.CanOrder"/>, and the dependency is the
/// point rather than an implementation detail. Age is <c>(arrival − the sender's clock) − offset</c>,
/// so it is only ever as good as the offset: with an unbounded one, a peer running three hours slow
/// and a sample held for three hours produce identical arithmetic and no amount of looking at the
/// numbers separates them. §3 had to be settled before §4 could mean anything.
/// <para>
/// Driven end to end against a peer written for it, streaming this product's frame shape: forty
/// prompt samples, one whose timestamp was four hours old, then twenty more prompt ones. The
/// receiving host flagged exactly the backfill, at <c>lateBySec = 14400.0</c>, and none of the 104
/// prompt samples around it.
/// </para>
/// </remarks>
public class ArrivalAgeTests
{
    private static readonly DateTime Arrived = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>An offset of <paramref name="offsetSec"/> known to <paramref name="spreadSec"/>.</summary>
    private static ClockOffsetEstimate Known(double offsetSec, double spreadSec) =>
        new(offsetSec, spreadSec, 8);

    [Fact]
    [Trait("Category", "Tier1")]
    public void ASampleObservedHereIsNotAskedHowOldItIs()
    {
        // One clock, so the question does not arise. Answering "prompt" would be a claim about a
        // comparison nobody made.
        ArrivalAge age = ArrivalAge.Determine(null, Arrived, ClockOffsetEstimate.Unmeasured);

        age.Kind.Should().Be(ArrivalKind.Local);
        age.IsLate.Should().BeFalse();
        age.IsDetermined.Should().BeFalse();
        age.LateBySec.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void WithoutABoundedOffsetTheAgeIsUndeterminedRatherThanPrompt()
    {
        // The failure this type exists to prevent. A four-hour-old sample and a peer four hours
        // behind are the same arithmetic, and calling either one prompt publishes a stale reading
        // as current -- §4's "an alert threshold crossed four hours ago that only surfaces now".
        ArrivalAge age = ArrivalAge.Determine(
            Arrived.AddHours(-4), Arrived, ClockOffsetEstimate.Unmeasured);

        age.Kind.Should().Be(ArrivalKind.Undetermined);
        age.IsLate.Should().BeFalse("this host cannot establish that, and must not assert it");
        age.IsDetermined.Should().BeFalse();
        age.LateBySec.Should().BeNull("a number here would be read as the answer");
        age.Describe().Should().Contain("cannot be established");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void OneObservationIsNotAnErrorBarAndSoIsNotAJudgement()
    {
        // The case that looks like knowledge and is not. An offset exists; nothing bounds it.
        var singleObservation = new ClockOffsetEstimate(0.002, null, 1);

        ArrivalAge.Determine(Arrived.AddHours(-4), Arrived, singleObservation)
            .Kind.Should().Be(ArrivalKind.Undetermined);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ABackfilledSampleIsCalledLateAndSaysByHowMuch()
    {
        ArrivalAge age = ArrivalAge.Determine(
            Arrived.AddHours(-4), Arrived, Known(offsetSec: 0.002, spreadSec: 0.001));

        age.Kind.Should().Be(ArrivalKind.Late);
        age.IsLate.Should().BeTrue();
        age.LateBySec.Should().BeApproximately(14400.0, 0.01);
        age.Describe().Should().Contain("before it arrived");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AnOrdinarySampleIsNotCalledLateForItsOwnTransit()
    {
        // The threshold is not a constant anybody chose. The offset estimate is the minimum
        // observation, so it has already absorbed the fastest transit; what is left for a prompt
        // sample is the variation, which is exactly what the spread measures. Against a live pair
        // this residual ran +0.2 ms to +0.8 ms with a spread of 0.81 ms.
        ArrivalAge age = ArrivalAge.Determine(
            Arrived.AddSeconds(-0.0006), Arrived, Known(offsetSec: 0.0002, spreadSec: 0.00081));

        age.Kind.Should().Be(ArrivalKind.Prompt);
        age.IsLate.Should().BeFalse();
        age.IsDetermined.Should().BeTrue("prompt is a finding, not an absence of one");
        age.LateBySec.Should().NotBeNull("the residual is still worth publishing");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ExactlyAtTheUncertaintyIsNotOutsideIt()
    {
        var offset = Known(offsetSec: 0.0, spreadSec: 0.5);

        ArrivalAge.Determine(Arrived.AddSeconds(-0.5), Arrived, offset)
            .Kind.Should().Be(ArrivalKind.Prompt, "the bound itself is inside the bound");
        ArrivalAge.Determine(Arrived.AddSeconds(-0.51), Arrived, offset)
            .Kind.Should().Be(ArrivalKind.Late);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ASampleAppearingToPredateItsOwnObservationIsNotCalledLate()
    {
        // Negative age: the sample looks like it arrived before it was taken. That is the offset
        // having moved under us, not a fresh reading and certainly not a stale one. Late means one
        // specific thing -- older than it appears -- and this is the opposite direction.
        ArrivalAge age = ArrivalAge.Determine(
            Arrived.AddSeconds(5), Arrived, Known(offsetSec: 0.0, spreadSec: 0.001));

        age.Kind.Should().Be(ArrivalKind.Prompt);
        age.LateBySec.Should().BeApproximately(-5.0, 0.01,
            "the number is still reported, because a consumer chasing a clock that jumped needs it");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AnUnusableArithmeticResultIsUndeterminedRatherThanZero()
    {
        ArrivalAge.Determine(Arrived.AddHours(-1), Arrived, new ClockOffsetEstimate(double.NaN, 0.5, 4))
            .Kind.Should().Be(ArrivalKind.Undetermined);
    }
}
