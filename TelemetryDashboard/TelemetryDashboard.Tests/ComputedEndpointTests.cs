using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Query;
using TelemetryDashboard.Core.Streaming;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// <c>/api/computed</c>: the quantities an operator cares about, and when it refuses to give them.
/// </summary>
/// <remarks>
/// The endpoint's value is not that it multiplies. It is that it will not multiply a voltage from
/// now by a current from three seconds ago and call the product a power.
/// </remarks>
public class ComputedEndpointTests
{
    private const double At = 1_000_000.0;

    /// <summary>One sample a second either side of the instant, on the named channels.</summary>
    private static SeriesStore StoreWith(params string[] channels)
    {
        var store = new SeriesStore();
        foreach (string channel in channels)
        {
            for (int i = -10; i <= 10; i++)
            {
                store.Append(channel, Value(channel), At + i);
            }
        }
        return store;
    }

    private static double Value(string channel) => channel.EndsWith("current", StringComparison.Ordinal)
        ? 25.0
        : 400.0;

    private static IReadOnlyList<ComputedChannel> Declare(params string[] declarations) =>
        declarations.Select(ComputedChannel.Parse).ToList();

    [Fact]
    public void AHostThatDeclaresNothingSaysSoRatherThanReturningAnEmptyList()
    {
        // An empty answer and "nobody configured any" are different facts, and only one of them
        // means the operator should restart with --computed.
        ComputedEndpoint.Result result =
            ComputedEndpoint.Compute(new SeriesStore(), Array.Empty<ComputedChannel>(), At, 30);

        result.Status.Should().Be("Error");
        result.Reason.Should().Contain("--computed");
    }

    [Fact]
    public void EveryInputPresentAtTheInstantGivesAValue()
    {
        ComputedEndpoint.Result result = ComputedEndpoint.Compute(
            StoreWith("SIM:A.dab.bus_voltage", "SIM:A.dab.input_current"),
            Declare("dab.p_in[W] = dab.bus_voltage * dab.input_current"),
            At, 30);

        ComputedEndpoint.ComputedValue channel = result.Channels.Single();
        channel.Status.Should().Be("Computed");
        channel.Value.Should().Be(10_000.0);
        channel.Unit.Should().Be("W");
        channel.Derived.Should().BeTrue("a computed value that cannot say so reads as a measurement");
        result.Available.Should().Be(1);
    }

    [Fact]
    public void AnswerNamesBothWhatWasWrittenAndWhatWasRead()
    {
        // The two differ on every real host, and the difference is where the mistakes are.
        ComputedEndpoint.Result result = ComputedEndpoint.Compute(
            StoreWith("SIM:A.dab.bus_voltage", "SIM:A.dab.input_current"),
            Declare("dab.p_in[W] = dab.bus_voltage * dab.input_current"),
            At, 30);

        result.Channels.Single().Inputs.Select(i => (i.Declared, i.Resolved)).Should().Equal(
            ("dab.bus_voltage", "SIM:A.dab.bus_voltage"),
            ("dab.input_current", "SIM:A.dab.input_current"));
    }

    [Fact]
    public void AnInputThatIsOnlyHeldIsRefusedEvenThoughItHasAValue()
    {
        // The heart of it. Both channels have a last-known value and the naive implementation
        // multiplies them; that product describes no instant that ever happened.
        SeriesStore store = StoreWith("SIM:A.dab.bus_voltage", "SIM:A.dab.input_current");

        ComputedEndpoint.Result result = ComputedEndpoint.Compute(
            store,
            Declare("dab.p_in[W] = dab.bus_voltage * dab.input_current"),
            // Past the last sample (At+10) but well inside the 30s window, so the buffer has
            // plenty to work with and still cannot bracket this instant. Written as At+5 first,
            // which is a sample -- the alignment was Exact and the value was correct, and the
            // assertion that would have made that pass was a wider tolerance.
            At + 15,
            30);

        ComputedEndpoint.ComputedValue channel = result.Channels.Single();
        channel.Value.Should().BeNull();
        channel.Status.Should().Be("Unavailable");
        channel.Reason.Should().Contain("would describe a different moment");
        channel.Inputs.Should().OnlyContain(i => i.Value != null,
            "the held values are reported, so a reader can see what was rejected and why");
    }

    [Fact]
    public void AnInputThatHasNeverReportedNamesItself()
    {
        ComputedEndpoint.Result result = ComputedEndpoint.Compute(
            StoreWith("SIM:A.dab.bus_voltage"),
            Declare("dab.p_in[W] = dab.bus_voltage * dab.input_current"),
            At, 30);

        result.Channels.Single().Reason.Should().Contain("dab.input_current");
        result.Available.Should().Be(0);
    }

    [Fact]
    public void TwoNodesReportingTheSameChannelIsRefusedRatherThanPickedFrom()
    {
        // Choosing one would compute a converter's power from another converter's current, and
        // the answer would look exactly like a correct one.
        ComputedEndpoint.Result result = ComputedEndpoint.Compute(
            StoreWith("SIM:A.dab.bus_voltage", "SIM:B.dab.bus_voltage"),
            Declare("x = dab.bus_voltage * 2"),
            At, 30);

        result.Channels.Single().Reason.Should()
            .Contain("2 nodes").And.Contain("[node].dab.bus_voltage");
    }

    [Fact]
    public void AnExplicitlyQualifiedNameSettlesTheAmbiguity()
    {
        ComputedEndpoint.Result result = ComputedEndpoint.Compute(
            StoreWith("SIM:A.dab.bus_voltage", "SIM:B.dab.bus_voltage"),
            Declare("x = [SIM:B].dab.bus_voltage * 2"),
            At, 30);

        result.Channels.Single().Value.Should().Be(800.0);
    }

    [Fact]
    public void AskingForOneIdAnswersOnlyThatOne()
    {
        ComputedEndpoint.Result result = ComputedEndpoint.Compute(
            StoreWith("SIM:A.dab.bus_voltage", "SIM:A.dab.input_current"),
            Declare("a[W] = dab.bus_voltage * dab.input_current", "b = dab.bus_voltage * 2"),
            At, 30, only: new[] { "b" });

        result.Channels.Select(c => c.Id).Should().Equal("b");
        result.Declared.Should().Be(2, "the host still declares two, whatever was asked for");
    }

    [Fact]
    public void AnIdNobodyDeclaredIsReportedRatherThanOmitted()
    {
        // Omitting it would make a typo look like a channel that went quiet.
        ComputedEndpoint.Result result = ComputedEndpoint.Compute(
            StoreWith("SIM:A.dab.bus_voltage"),
            Declare("b = dab.bus_voltage * 2"),
            At, 30, only: new[] { "nobody.declared.this" });

        ComputedEndpoint.ComputedValue answer = result.Channels.Single(c => c.Id == "nobody.declared.this");
        answer.Status.Should().Be("Unavailable");
        answer.Reason.Should().Contain("no computed channel is declared");
    }

    [Fact]
    public void EveryRowInOneReplyIsComputedAtTheSameInstant()
    {
        // The property that separates this from reading the latest of each channel. If each
        // expression were evaluated at its own moment, these two would not agree.
        SeriesStore store = StoreWith("SIM:A.v", "SIM:A.i");

        ComputedEndpoint.Result result = ComputedEndpoint.Compute(
            store,
            Declare("p[W] = v * i", "eff[%] = 100 * v * i / (v * i)"),
            At, 30);

        result.Channels.Should().OnlyContain(c => c.Value != null);
        result.Channels.Single(c => c.Id == "eff").Value.Should().Be(100.0);
    }
}
