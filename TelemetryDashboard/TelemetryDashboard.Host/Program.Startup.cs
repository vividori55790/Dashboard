using System;
using System.Text;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Host.Startup;

namespace TelemetryDashboard.Host;

public static partial class Program
{
    /// <summary>
    /// Everything that happens before anything is served, and the exit codes that end the run there.
    /// </summary>
    /// <remarks>
    /// Split out of <c>Main</c> when it reached the 150-line rule with no room for the encoding
    /// line below. Splitting on this boundary rather than an arbitrary one: everything here decides
    /// whether there is a run at all, and everything left in <c>Main</c> is the run.
    /// </remarks>
    /// <returns>An exit code when the run ends here, or null to continue.</returns>
    private static int? PreFlight(string[] args, out HostOptions options)
    {
        UseUtf8WhenRedirected();

        options = new HostOptions();

        // Before anything binds a socket. Every subcommand ends rather than serving, and Subcommands
        // carries the account of why that ordering matters.
        if (Subcommands.Run(args) is { } subcommandExit) return subcommandExit;

        options = CommandLineParser.Parse(args, EnvironmentVariables.Read());

        if (options.ShowHelp)
        {
            Console.Out.Write(UsageText.Render());
            return 0;
        }

        if (options.Error is not null)
        {
            Console.Error.WriteLine($"telemetry-host: {options.Error}");
            Console.Error.WriteLine("Run with --help for the accepted arguments.");
            return ExitUsage;
        }

        return null;
    }

    /// <summary>
    /// Writes UTF-8 when the output is going somewhere other than a console.
    /// </summary>
    /// <remarks>
    /// Measured rather than assumed: <c>telemetry-host &gt; host.log</c> on this project's own
    /// development machine produced a file that does not decode as UTF-8 at all. It is the console's
    /// legacy code page — CP949 there — so the Korean profile names in the banner are mojibake to
    /// anything that reads the file as UTF-8, which is every editor, every CI log viewer and every
    /// bug report. Characters with no mapping in that code page are not mangled but *lost*: the em
    /// dashes in the start-up text simply are not in the file.
    /// <para>
    /// Conditional on redirection, which removes the trade-off rather than balancing it. An
    /// interactive console keeps exactly the behaviour it has today, including on a terminal whose
    /// font or code page would render UTF-8 badly; a redirected stream has no terminal to upset and
    /// its consumer is a file or a pipe, where UTF-8 is not a preference.
    /// </para>
    /// <para>
    /// No BOM. A log is appended to, concatenated and tailed, and a byte-order mark in the middle of
    /// a stream is a defect rather than a hint.
    /// </para>
    /// </remarks>
    private static void UseUtf8WhenRedirected()
    {
        if (!Console.IsOutputRedirected && !Console.IsErrorRedirected) return;

        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (Exception ex) when (ex is System.IO.IOException or PlatformNotSupportedException)
        {
            // A handle that will not take an encoding is not a reason to refuse to start. The log
            // is then whatever the platform chose, which is exactly today's behaviour.
        }
    }
}
