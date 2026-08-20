using System;
using System.Linq;
using System.Text;
using FluentAssertions;
using TelemetryDashboard.Core.Parsers;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The checksum has to mean the same thing on both sides of the wire.
/// </summary>
/// <remarks>
/// A device computes it over the bytes it transmits — the generated firmware header says so
/// literally, <c>cs ^= ((const uint8_t*)(b))[i]</c> — while this application computes it over a
/// string it has already decoded. Those two agree for ASCII and diverged for everything else,
/// because the char overload truncated each character to its low byte.
/// <para>
/// The consequence was not a wrong number on screen. A checksum mismatch is reported as a corrupt
/// frame, and a corrupt frame is dropped, so the symptom was a channel that simply never appeared
/// — indistinguishable from a sensor that was not wired up. The default profile ships a channel
/// whose unit is °C, which is enough to reach it.
/// </para>
/// </remarks>
public class ChecksumEncodingTests
{
    [Theory]
    [InlineData("TELE,NODE_1,ambient.temperature,25.4,C")]
    [InlineData("TELE,NODE_1,ambient.temperature,25.4,°C")]   // U+00B0, two bytes in UTF-8
    [InlineData("TELE,NODE_1,machine.speed,1200,회전수")]      // three bytes per character
    [InlineData("TELE,NODE_1,power.output,12.5,µW")]           // micro sign
    [InlineData("TELE,NODE_1,note,1,\U0001F525")]              // outside the BMP: a surrogate pair
    public void TheCharAndByteFormsAgreeOnEveryPayload(string body)
    {
        byte overChars = XorChecksum.Calculate(body.AsSpan());
        byte overBytes = XorChecksum.Calculate(Encoding.UTF8.GetBytes(body));

        overChars.Should().Be(overBytes,
            "the checksum covers the bytes that travel, and the string is only how this process happens to hold them");
    }

    [Fact]
    public void AFrameCarryingANonAsciiUnitIsAccepted()
    {
        const string body = "TELE,KILN_A,zone3.temperature,940.5,°C";
        byte checksum = XorChecksum.Calculate(Encoding.UTF8.GetBytes(body));
        string frame = $"${body}*{checksum:X2}";

        XorChecksum.ValidateSpan(frame.AsSpan(), out _).Should().BeTrue(
            "a device that sends a degree sign is not sending a corrupt frame");
    }

    [Fact]
    public void ATamperedNonAsciiFrameIsStillRejected()
    {
        // Without this the test above would also pass on a checksum that accepted anything.
        const string body = "TELE,KILN_A,zone3.temperature,940.5,°C";
        byte checksum = XorChecksum.Calculate(Encoding.UTF8.GetBytes(body));
        string frame = $"${body}*{checksum:X2}".Replace("940.5", "940.6");

        XorChecksum.ValidateSpan(frame.AsSpan(), out _).Should().BeFalse();
    }

    [Fact]
    public void TheAsciiResultIsUnchangedFromThePlainByteXor()
    {
        // The fix must not have moved the checksum of any frame that already worked, or every
        // deployed device would start disagreeing with this build.
        const string body = "TELE,MCU_NODE_1,TEMP,25.40,C";

        byte expected = 0;
        foreach (char c in body) expected ^= (byte)c;

        XorChecksum.Calculate(body.AsSpan()).Should().Be(expected);
    }
}
