namespace TelemetryDashboard.Host.Configuration;

/// <summary>Help for the <c>sniff</c> subcommand.</summary>
/// <remarks>
/// Written as the answer to a question rather than as a list of flags, because the person reading
/// it has a device on a bench that is not showing up and does not yet know this command is what
/// they want.
/// </remarks>
public static class SniffUsageText
{
    public static string Render() =>
        """
        telemetry-host sniff — find out what your device is actually sending.

        Usage:
          telemetry-host sniff --serial COM3 --profile dab-psfb-ups
          telemetry-host sniff --sse http://bench:8085/stream --for 30s --out bench.json

        A real MCU sends its own names, in its own units, on its own frame tag. This listens for a
        few seconds, writes down every channel that arrived, and drafts the rules file that renames
        them into the profile's terms. Everything it could not decide is left commented out with
        the evidence beside it, so the file is filled in rather than written from nothing.

        Nothing is published, recorded or archived, and no port is opened for the console.

          --for <duration>   How long to listen. 15s by default; 20, 30s and 2m all read.
          --out <file>       Where to write the draft. rules.json by default.
          --force            Replace an existing output file instead of refusing.
          --verify           Check the rules already in force instead of drafting new ones.
                             Writes nothing, and exits non-zero while any declared channel is
                             still receiving no readings — so a commissioning step can be gated
                             on it rather than read by whoever is watching.

        Every other flag is the serving host's, and means what it means there:

          --serial <port>    The port the device is on, e.g. COM3 or /dev/ttyUSB0.
          --baud <rate>      Baud rate, when it is not the default.
          --sse <url>        A server-sent-events stream instead of a port.
          --replay <file>    A recording, which is how this is exercised without hardware.
          --profile <id>     The profile to judge the readings against. Without one the draft
                             lists what arrived and has nothing to map it onto.
          --rules <file>     Rules to listen through. Pair it with --verify to check a file you
                             have already written, against the device rather than against itself.

        The loop this closes:

          telemetry-host sniff --serial COM3 --profile dab-psfb-ups        # draft
          # fill in what it left commented out, then ask the device whether you got it right
          telemetry-host sniff --serial COM3 --profile dab-psfb-ups --rules rules.json --verify
          telemetry-host --serial COM3 --profile dab-psfb-ups --rules rules.json

        """;
}
