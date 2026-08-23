using TelemetryDashboard.Core.Ingest;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Whether two units are the same quantity at a different scale.
/// </summary>
/// <remarks>
/// This is the one thing the rule drafter is allowed to fill in without being asked, so what it
/// refuses matters more than what it returns. Millivolts to volts is 0.001 by definition; anything
/// it cannot derive that way is left for the operator, because a wrong scale is the failure that
/// hides best — the band still exists, the chart still moves, and the reading it judges is a
/// thousand times too large.
/// </remarks>
public class UnitScaleTests
{
    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData("mV", "V", 0.001)]
    [InlineData("V", "mV", 1000.0)]
    [InlineData("mA", "A", 0.001)]
    [InlineData("kW", "W", 1000.0)]
    [InlineData("uV", "V", 0.000001)]
    [InlineData("V", "V", 1.0)]
    [InlineData("%", "%", 1.0)]
    public void TheSameQuantityAtADifferentScaleIsArithmetic(string from, string to, double expected)
    {
        UnitScale.Between(from, to).Should().BeApproximately(expected, expected * 1e-9);
    }

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData("V", "A")]
    [InlineData("degC", "V")]
    [InlineData("%", "V")]
    [InlineData("V", "")]
    [InlineData("", "V")]
    [InlineData(null, "V")]
    public void TwoUnrelatedUnitsAreLeftAlone(string? from, string to)
    {
        UnitScale.Between(from, to).Should().BeNull();
    }

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData("min", "in")]
    [InlineData("mol", "ol")]
    [InlineData("kph", "ph")]
    public void APrefixIsNotFoundInWordsThatMerelyStartWithOne(string from, string to)
    {
        // Why the base units are an allowlist. A prefix parser turned loose on arbitrary text reads
        // "min" as milli-inches and would then scale a reading by a thousand for saying so.
        UnitScale.Between(from, to).Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void MilliAndMegaAreNotTheSameLetter()
    {
        UnitScale.Between("mW", "W").Should().Be(0.001);
        UnitScale.Between("MW", "W").Should().Be(1e6);
    }

    [Theory]
    [Trait("Category", "Tier2")]
    [InlineData(0.001, "0.001")]
    [InlineData(1000.0, "1000")]
    [InlineData(0.000001, "0.000001")]
    public void AGainIsWrittenAsANumberAPersonCanRead(double gain, string expected)
    {
        // It ends up in a configuration file somebody edits. 1E-06 is a number a machine wrote.
        UnitScale.Format(gain).Should().Be(expected);
    }
}
