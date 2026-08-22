using TelemetryDashboard.Core.Storage;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Reading the one setting in this product that destroys data.
/// </summary>
/// <remarks>
/// Everything here refuses rather than guesses, and that is the whole design. A policy misread by a
/// day is not caught by a test run or a code review — it is caught weeks later by the data that is
/// no longer there. So a clause nobody can read fails and says what was expected, instead of a host
/// starting up having quietly decided a different number than the person typing meant.
/// <para>
/// Driven against the running host: every refusal below is the message it actually printed.
/// </para>
/// </remarks>
public class RetentionSpecTests
{
    private static RetentionPolicy Parse(string spec)
    {
        RetentionSpec.TryParse(spec, out RetentionPolicy policy, out string? error).Should().BeTrue(error);
        return policy;
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void EveryTierAndUnitAnOperatorMightWriteIsRead()
    {
        RetentionPolicy policy = Parse("raw=7d,second=2h,minute=90d,hour=2y");

        policy.Enabled.Should().BeTrue();
        policy.RawRetention.Should().Be(TimeSpan.FromDays(7));
        policy.SecondRetention.Should().Be(TimeSpan.FromHours(2));
        policy.MinuteRetention.Should().Be(TimeSpan.FromDays(90));
        policy.HourRetention.Should().Be(TimeSpan.FromDays(730));
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ATierLeftOutIsKeptForeverRatherThanDefaultedToSomething()
    {
        // The safe direction for a setting whose mistakes are permanent. Somebody who wrote only a
        // raw clause has said nothing about their rollups, and inventing a number for them would be
        // deleting data they never mentioned.
        RetentionPolicy policy = Parse("raw=7d");

        policy.SecondRetention.Should().BeNull();
        policy.MinuteRetention.Should().BeNull();
        policy.HourRetention.Should().BeNull();
    }

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData("raw=7", "needs a number and a unit")]
    [InlineData("raw=1w,raw=2d", "given twice")]
    [InlineData("week=7d", "is not a tier")]
    [InlineData("raw=0d", "must be greater than zero")]
    [InlineData("raw=seven d", "is not a number")]
    [InlineData("raw 7d", "is not a clause")]
    [InlineData("raw=7x", "is not a unit")]
    [InlineData("", "a retention policy is required")]
    [InlineData(null, "a retention policy is required")]
    public void AClauseNobodyCanReadIsRefusedWithWhatWasExpected(string? spec, string expected)
    {
        RetentionSpec.TryParse(spec, out RetentionPolicy policy, out string? error).Should().BeFalse();

        error.Should().Contain(expected);
        policy.Enabled.Should().BeFalse("a policy that failed to parse must never delete anything");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ZeroIsRefusedRatherThanReadAsKeepNothing()
    {
        // The reading that destroys everything on the first prune is the wrong one to guess at.
        // Somebody writing raw=0d almost certainly means to turn retention off for that tier.
        RetentionSpec.TryParse("raw=0d", out _, out string? error).Should().BeFalse();
        error.Should().Contain("leave the tier out to keep it forever");
    }

    [Theory]
    [Trait("Category", "Tier2")]
    [InlineData("raw=30s", 30)]
    [InlineData("raw=2m", 120)]
    [InlineData("raw=1h", 3600)]
    [InlineData("raw=1d", 86400)]
    [InlineData("raw=1w", 604800)]
    public void EachUnitMeansWhatItLooksLike(string spec, double seconds)
    {
        Parse(spec).RawRetention.Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void SpacingAndCaseAreForgivenBecauseTheyChangeNothing()
    {
        // Refusing what cannot be read is not the same as refusing what reads perfectly well. A
        // parser strict about whitespace teaches people to distrust the strictness that matters.
        RetentionPolicy spaced = Parse(" RAW = 7d , Minute = 90d ");

        spaced.RawRetention.Should().Be(TimeSpan.FromDays(7));
        spaced.MinuteRetention.Should().Be(TimeSpan.FromDays(90));
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void APolicyThatWasNeverAskedForKeepsEverything()
    {
        RetentionPolicy.Disabled.Enabled.Should().BeFalse();
        RetentionPolicy.Disabled.RetentionFor(RollupInterval.Minute).Should().BeNull();
    }
}
