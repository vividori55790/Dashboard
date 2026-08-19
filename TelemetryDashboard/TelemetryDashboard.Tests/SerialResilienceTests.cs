using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Events;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Infrastructure.Serial;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Covers what happens to a serial link when things go wrong rather than when they go right.
/// </summary>
/// <remarks>
/// The fault-to-reconnect loop end to end needs a physical port that can be unplugged mid-run, so
/// it is not asserted here and is not claimed to be covered. What is covered is everything that
/// made recovery impossible before it: a partial frame spliced onto the next one, and a failed
/// connection leaving a phantom worker behind that makes every later attempt return success while
/// no bytes arrive.
/// </remarks>
public class SerialResilienceTests
{
    private static List<string> Feed(SerialLineAssembler assembler, string chunk)
    {
        var lines = new List<string>();
        assembler.Append(Encoding.UTF8.GetBytes(chunk), lines.Add);
        return lines;
    }

    [Fact]
    public void AFrameSplitAcrossTwoReadsIsRejoined_NotCutInHalf()
    {
        var assembler = new SerialLineAssembler();

        Feed(assembler, "$TELE,MCU_A,te").Should().BeEmpty("half a frame is not a frame");
        Feed(assembler, "mp,41.9,C\n").Should().ContainSingle()
            .Which.Should().Be("$TELE,MCU_A,temp,41.9,C");
    }

    [Fact]
    public void TwoFramesArrivingInOneReadAreBothDelivered()
    {
        var assembler = new SerialLineAssembler();

        Feed(assembler, "first\nsecond\n").Should().Equal("first", "second");
    }

    [Fact]
    public void CarriageReturnsAreStrippedAndBlankLinesAreNotEmitted()
    {
        var assembler = new SerialLineAssembler();

        Feed(assembler, "value\r\n\r\nnext\r\n").Should().Equal("value", "next");
    }

    [Fact]
    public void ALineThatNeverEndsIsAbandonedRatherThanGrowingWithoutBound()
    {
        var assembler = new SerialLineAssembler();

        Feed(assembler, new string('x', SerialLineAssembler.MaxLineLength + 10)).Should().BeEmpty();

        assembler.OverlongDiscards.Should().BeGreaterThan(0, "the discard must be countable, not invisible");
        assembler.Pending.Should().BeLessThan(SerialLineAssembler.MaxLineLength);
    }

    [Fact]
    public void ResetDropsThePartialLineSoAReconnectDoesNotSpliceTwoSessions()
    {
        var assembler = new SerialLineAssembler();

        Feed(assembler, "$TELE,MCU_A,te");
        assembler.Reset();

        Feed(assembler, "mp,41.9,C\n").Should().ContainSingle().Which.Should().Be("mp,41.9,C");
    }

    [Fact]
    public async Task AFailedConnectionLeavesNoPhantomWorkerBehind()
    {
        await using var manager = new MultiPortSerialManager();
        const string absent = "COM_DOES_NOT_EXIST_999";

        (await manager.ConnectPortAsync(absent, 115200)).Should().BeFalse();
        manager.ActivePorts[absent].Should().Be(PortConnectionStatus.Disconnected);

        // The connect path returns early for a port it already holds. A worker cached from a failed
        // attempt would make this second call report success while nothing was ever opened.
        (await manager.ConnectPortAsync(absent, 115200)).Should().BeFalse();
    }

    [Fact]
    public void AFaultReportsWhatTheDriverSaid_NotJustThatSomethingBroke()
    {
        new SerialPortFaultEventArgs("COM3", new System.IO.IOException("The device is not connected."))
            .Describe().Should().Contain("COM3").And.Contain("IOException").And.Contain("not connected");

        new SerialPortFaultEventArgs("COM3", cause: null)
            .Describe().Should().Contain("closed unexpectedly");
    }
}
