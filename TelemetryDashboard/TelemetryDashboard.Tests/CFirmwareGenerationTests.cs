using System.Text.RegularExpressions;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Parsers;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The generated header and driver have to describe the same device, and speak this product's wire.
/// </summary>
/// <remarks>
/// Found while giving the generator dialog a real configuration. The header wrote a struct from the
/// configured channels; the driver read <c>data-&gt;temperature</c> and <c>data-&gt;vibration</c>
/// whatever the configuration said, sent two values positionally where the parser expects a named
/// channel and a unit, and ended every frame with the literal <c>*00</c> while the header defined a
/// checksum macro nothing called. For any configuration other than the worked example the two files
/// did not compile together, and had they compiled, every frame would have failed validation.
/// <para>
/// Not verified: that this compiles. There is no C toolchain on the machine these were written on,
/// so the pair is checked by reading them — the driver's field references against the header's
/// declarations — rather than by a compiler. Nothing here has been near an MCU.
/// </para>
/// </remarks>
public class CFirmwareGenerationTests
{
    private static readonly CHeaderGenerator Generator = new();

    /// <summary>The bundled converter profile as firmware configuration, as the shell builds it.</summary>
    private static SensorNodeConfig RigConfig()
    {
        MonitoringProfile profile = MonitoringProfileLibrary.PowerConverterUps;

        return new SensorNodeConfig
        {
            NodeId = profile.Nodes.Count > 0 ? profile.Nodes[0].Id : profile.Id,
            TagPrefix = DefaultRoutingRules.TelemetryTag,
            Variables = profile.Channels
                .Select(c => new VariableDefinition { Name = c.Id, Unit = c.Unit, DataType = "float" })
                .ToList()
        };
    }

    private static IEnumerable<string> StructMembers(string header) =>
        Regex.Matches(header, @"^\s+\w+\s+(\w+);", RegexOptions.Multiline).Select(m => m.Groups[1].Value);

    private static IEnumerable<string> DereferencedFields(string driver) =>
        Regex.Matches(driver, @"data->(\w+)").Select(m => m.Groups[1].Value);

    [Fact]
    [Trait("Category", "Firmware")]
    public void EveryFieldTheDriverReadsIsDeclaredByTheHeader()
    {
        // The defect in one assertion: a driver naming a member no struct declares does not build,
        // and the operator finds out in their toolchain rather than here.
        SensorNodeConfig config = RigConfig();

        string[] declared = StructMembers(Generator.GenerateHeader(config)).ToArray();
        string[] read = DereferencedFields(Generator.GenerateDriverCode(config)).ToArray();

        read.Should().NotBeEmpty("the rig declares channels, so the driver must send some");
        read.Should().BeSubsetOf(declared);
    }

    [Fact]
    [Trait("Category", "Firmware")]
    public void OneNamedFrameIsSentForEveryConfiguredChannel()
    {
        SensorNodeConfig config = RigConfig();

        string driver = Generator.GenerateDriverCode(config);

        string[] sent = Regex.Matches(driver, @"Telemetry_SendField\(""([^""]+)""")
            .Select(m => m.Groups[1].Value).ToArray();
        sent.Should().Equal(config.Variables.Select(v => v.Name));
    }

    [Theory]
    [Trait("Category", "Firmware")]
    [InlineData("STM32")]
    [InlineData("ESP32")]
    [InlineData("ARDUINO")]
    public void EveryTargetComputesTheChecksumRatherThanSendingZero(string platform)
    {
        // The send routine used to be repeated once per platform, so a checksum fixed in one branch
        // would have left the other two transmitting frames this product rejects.
        string driver = Generator.GenerateDriverCode(RigConfig(), platform);

        driver.Should().Contain("CALCULATE_XOR_CHECKSUM(body");
        driver.Should().NotContain("*00");
    }

    [Fact]
    [Trait("Category", "Firmware")]
    public void AFrameInTheGeneratedFormatIsAcceptedByThisProductsOwnParser()
    {
        // Rendered from the format string the generator actually emits, which is asserted first --
        // so a change to the format fails here rather than leaving this checking a shape nothing
        // sends. The checksum is composed with XorChecksum.Calculate, which
        // F08_CalculateXorChecksumMacro_Verification already shows is the macro's arithmetic.
        string driver = Generator.GenerateDriverCode(RigConfig());

        System.Text.RegularExpressions.Match body = Regex.Match(driver, @"snprintf\(body, sizeof\(body\), ""([^""]+)""");
        body.Groups[1].Value.Should().Be("%s,%s,%s,%.3f,%s");
        Regex.Match(driver, @"snprintf\(line, sizeof\(line\), ""([^""]+)""")
            .Groups[1].Value.Should().Be(@"$%s*%02X\r\n");

        string content = "TELE,COM3,psfb.output_voltage,48.125,V";
        string frame = $"${content}*{XorChecksum.Calculate(content.AsSpan()):X2}\r\n";

        PrefixParser.TryParse(
            new RawPacket("COM3", frame, DateTime.UtcNow),
            DefaultRoutingRules.Create()[0],
            out List<TelemetryPacket> parsed).Should().BeTrue();

        parsed.Should().ContainSingle();
        parsed[0].NodeId.Should().Be("COM3");
        parsed[0].Variable.Should().Be("psfb.output_voltage");
        parsed[0].Value.Should().Be(48.125);
        parsed[0].Unit.Should().Be("V");
    }

    [Fact]
    [Trait("Category", "Firmware")]
    public void ChannelsThatSanitiseToTheSameIdentifierStillGetDistinctFields()
    {
        // Every run of punctuation collapses to one underscore, so these three arrive at the same
        // C identifier -- three struct members with one name, which no compiler accepts.
        var config = new SensorNodeConfig
        {
            Variables = new[] { "psfb.out_v", "psfb-out-v", "psfb out v" }
                .Select(n => new VariableDefinition { Name = n, Unit = "V" }).ToList()
        };

        string[] declared = StructMembers(Generator.GenerateHeader(config)).ToArray();

        declared.Should().OnlyHaveUniqueItems();
        DereferencedFields(Generator.GenerateDriverCode(config)).Should().BeSubsetOf(declared);
    }

    [Fact]
    [Trait("Category", "Firmware")]
    public void TheHeaderStaysAsciiWhateverAChannelIsCalled()
    {
        // A channel name is arbitrary text and the header is all identifiers, so nothing of it
        // belongs there raw. "°C" would make the file non-ASCII for a toolchain that may not read
        // it that way, and a name containing "*/" would close a comment early and break the build.
        // The driver carries the raw names because they are what goes on the wire, and says so.
        var config = new SensorNodeConfig
        {
            Variables = new[] { "온도", "duty */ break", "efficiency" }
                .Select(n => new VariableDefinition { Name = n, Unit = "°C" }).ToList()
        };

        string header = Generator.GenerateHeader(config);

        header.Should().MatchRegex("^[\x00-\x7F]*$", "the header is identifiers and macros only");
        header.Should().NotContain("*/ break");
    }

    [Fact]
    [Trait("Category", "Firmware")]
    public void AConfigurationWithNoChannelsSendsNothingRatherThanInventingTwo()
    {
        string driver = Generator.GenerateDriverCode(new SensorNodeConfig());

        Regex.Matches(driver, @"Telemetry_SendField\(""").Should().BeEmpty("there is nothing to send");
        driver.Should().NotContain("static void Telemetry_SendField",
            "a helper nothing calls is an unused-function warning");
        driver.Should().Contain("(void)data;", "an unused parameter is a warning on every toolchain");
    }
}
