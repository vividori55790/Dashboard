using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Plugins;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Declaring and evaluating a channel that no instrument reports.
/// </summary>
/// <remarks>
/// The behaviour under test is mostly refusal. An expression engine that answers every question
/// with a number is worse than none on a dashboard, because a wrong reading is indistinguishable
/// from a right one and both get acted on.
/// </remarks>
public class ComputedChannelTests
{
    [Fact]
    public void ADeclarationCarriesItsIdUnitAndInputs()
    {
        ComputedChannel channel = ComputedChannel.Parse(
            "psfb.efficiency[%] = 100 * psfb.output_voltage * psfb.output_current / (dab.bus_voltage * dab.input_current)");

        channel.Id.Should().Be("psfb.efficiency");
        channel.Unit.Should().Be("%");
        channel.Inputs.Should().Equal(
            "psfb.output_voltage", "psfb.output_current", "dab.bus_voltage", "dab.input_current");
    }

    [Fact]
    public void AChannelUsedTwiceIsListedOnce()
    {
        // Otherwise a formula would subscribe to the same series twice and report it twice.
        ComputedChannel.Parse("x = a.b + a.b * 2").Inputs.Should().Equal("a.b");
    }

    [Fact]
    public void AnUnqualifiedNameKeepsItsDots()
    {
        // The name has to come back out spelled exactly as the series store spells it, because
        // that is the string the resolver will look for.
        ComputedChannel.Parse("x = dab.bus_voltage * 2").Inputs.Should().Equal("dab.bus_voltage");
    }

    [Fact]
    public void ANodeCanBeNamedExplicitly()
    {
        ComputedChannel channel = ComputedChannel.Parse("x = [SIM:COM3].dab.bus_voltage * 2");
        channel.Inputs.Should().Equal("SIM:COM3.dab.bus_voltage");
    }

    [Theory]
    [InlineData("", "id[unit] = expression")]
    [InlineData("   ", "id[unit] = expression")]
    [InlineData("just.a.name", "names no expression")]
    [InlineData("x =", "nothing on the right")]
    [InlineData("= a.b", "nothing on the left")]
    [InlineData("x[V = a.b", "never closes it")]
    [InlineData("x = 2 * 3", "reads no channel")]
    [InlineData("a.b = a.b * 2", "defined in terms of itself")]
    [InlineData("x = power(a.b, 2)", "Unknown function 'power'")]
    [InlineData("x = min(a.b)", "takes 2 argument")]
    [InlineData("x = sqrt(a.b, 2)", "takes 1 argument")]
    public void AMalformedDeclarationIsRefusedWhereItIsWritten(string declaration, string expected)
    {
        // Every one of these used to be accepted somewhere. The unknown function answered 0.0 and
        // the wrong arity answered its first argument, so a typo became a channel that read a
        // plausible number forever and nothing ever said otherwise.
        Action parse = () => ComputedChannel.Parse(declaration);

        parse.Should().Throw<FormatException>().WithMessage($"*{expected}*");
    }

    [Fact]
    public void EveryInputPresentGivesTheArithmeticAnswer()
    {
        ComputedChannel channel = ComputedChannel.Parse("p[W] = v.bus * i.bus");

        double? value = channel.Evaluate(id => id switch
        {
            "v.bus" => 400.0,
            "i.bus" => 25.0,
            _ => null
        });

        value.Should().Be(10_000.0);
    }

    [Fact]
    public void OneMissingInputMakesTheWholeExpressionUnknown()
    {
        // Not zero. A product with a missing factor is exactly 0 and a quotient with a missing
        // denominator is infinite, and both of those print.
        ComputedChannel channel = ComputedChannel.Parse("p[W] = v.bus * i.bus");

        channel.Evaluate(id => id == "v.bus" ? 400.0 : (double?)null).Should().BeNull();
    }

    [Fact]
    public void AZeroDenominatorHasNoValueRatherThanAnInfiniteOne()
    {
        ComputedChannel channel = ComputedChannel.Parse("eff[%] = 100 * a.out / a.in");

        channel.Evaluate(id => id == "a.out" ? 900.0 : 0.0).Should().BeNull(
            "an efficiency measured while the input current is zero is unknown, not 1e308");
    }

    [Fact]
    public void ARootOfANegativeHasNoValue()
    {
        ComputedChannel.Parse("x = sqrt(a.b)").Evaluate(_ => -4.0).Should().BeNull();
    }

    [Fact]
    public void AZeroInputIsStillAReading()
    {
        // The distinction the whole nullable path exists for: a channel reading zero volts is not
        // a channel that has said nothing.
        ComputedChannel.Parse("x = a.b + 5").Evaluate(_ => 0.0).Should().Be(5.0);
    }

    [Fact]
    public void FunctionsAndPrecedenceBehaveAsWritten()
    {
        ComputedChannel channel = ComputedChannel.Parse("x = max(a.b, 2) + 3 * 4 ^ 2");

        channel.Evaluate(_ => 7.0).Should().Be(7.0 + 3 * 16);
    }

    [Fact]
    public void TheAstCanReportWhatItReadsWithoutRunning()
    {
        // Needed before evaluation, to know which series to align.
        AstNode ast = FormulaEvaluator.Parse("abs(a.b) + max(c.d, e.f)");
        var found = new List<VariableRef>();

        ast.CollectVariables(found);

        found.Select(v => v.ToString()).Should().Equal("a.b", "c.d", "e.f");
    }
}
