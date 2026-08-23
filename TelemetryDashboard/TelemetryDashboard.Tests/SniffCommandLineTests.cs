using TelemetryDashboard.Host.Configuration;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The command an operator types when their device is not showing up.
/// </summary>
/// <remarks>
/// Its own flags are three; everything else is the serving host's vocabulary, handed to the same
/// parser the host uses. That is not tidiness — the whole value of this command is that it hears
/// what the real run will hear, and a second parser understanding <c>--serial</c> even slightly
/// differently would draft a file for a stream nobody is going to have.
/// </remarks>
public class SniffCommandLineTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void TheVerbSelectsItAndNothingElseDoes()
    {
        SniffCommandLine.Matches(["sniff", "--serial", "COM3"]).Should().BeTrue();
        SniffCommandLine.Matches(["--serial", "COM3"]).Should().BeFalse();
        SniffCommandLine.Matches([]).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheSourceFlagsReachTheHostsOwnParserUntouched()
    {
        SniffCommandLine command = SniffCommandLine.Parse(
            ["sniff", "--serial", "COM7", "--baud", "230400", "--profile", "dab-psfb-ups"]);

        command.Error.Should().BeNull();
        command.Source.SerialPort.Should().Be("COM7");
        command.Source.BaudRate.Should().Be(230400);
        command.Source.ProfileId.Should().Be("dab-psfb-ups");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ItsOwnFlagsAreNotPassedOnAsUnknownArguments()
    {
        SniffCommandLine command = SniffCommandLine.Parse(
            ["sniff", "--sse", "http://bench/stream", "--for", "30s", "--out", "bench.json", "--force"]);

        command.Error.Should().BeNull();
        command.Duration.Should().Be(TimeSpan.FromSeconds(30));
        command.OutputPath.Should().Be("bench.json");
        command.Force.Should().BeTrue();
    }

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData("20", 20)]
    [InlineData("30s", 30)]
    [InlineData("2m", 120)]
    [InlineData("1.5", 1.5)]
    public void ADurationReadsTheWayAnOperatorWouldWriteIt(string text, double seconds)
    {
        SniffCommandLine.TryDuration(text, out TimeSpan duration).Should().BeTrue();
        duration.Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("0")]
    [InlineData("-5s")]
    [InlineData("5h")]
    public void ADurationNobodyCanReadIsRefusedRatherThanDefaulted(string text)
    {
        // Silently listening for fifteen seconds when somebody asked for five minutes would leave
        // them concluding their device only sends four channels.
        SniffCommandLine.TryDuration(text, out _).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AMissingValueIsNamedAlongWithWhatWasExpected()
    {
        SniffCommandLine.Parse(["sniff", "--for"]).Error.Should().Contain("--for needs a duration");
        SniffCommandLine.Parse(["sniff", "--out"]).Error.Should().Contain("--out needs a file");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheDefaultsAreTheOnesSomebodyWouldHaveTyped()
    {
        SniffCommandLine command = SniffCommandLine.Parse(["sniff", "--serial", "COM3"]);

        command.Duration.Should().Be(SniffCommandLine.DefaultDuration);
        command.OutputPath.Should().Be("rules.json");
        command.Force.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheHelpNamesTheCommandThatComesAfterIt()
    {
        // Somebody reading this has a device that is not showing up. Ending with the run command
        // that uses what they just wrote is the difference between a tool and a step.
        string help = SniffUsageText.Render();

        help.Should().Contain("--rules rules.json");
        help.Should().Contain("--for");
    }
}
