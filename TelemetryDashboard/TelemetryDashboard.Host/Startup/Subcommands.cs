using TelemetryDashboard.Host.Backtest;
using TelemetryDashboard.Host.Configuration;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// The words that make this executable do something other than serve telemetry.
/// </summary>
/// <remarks>
/// Every subcommand here shares one property, and it is the reason they are dispatched before
/// anything else in <see cref="Program"/>: they <em>end</em>. Installing an extension runs a third
/// party's code; replaying a price file prints a result. Neither binds a socket, and a process that
/// had already started serving would leave an operator unable to say whether the install happened
/// before or after the host began running a stranger's code — or holding a port open for the length
/// of a backtest nobody is watching over the network.
/// <para>
/// Collected into one list because there are two of them now and the next one is cheaper to add
/// here than to thread through the entry point again. The order is the order they are tried in; the
/// verbs do not overlap, so it does not currently matter, and it is fixed rather than incidental so
/// that it cannot start to.
/// </para>
/// </remarks>
internal static class Subcommands
{
    /// <summary>
    /// Runs whichever subcommand <paramref name="args"/> names, or null when it names none.
    /// </summary>
    /// <remarks>
    /// Null rather than a sentinel exit code, so the caller cannot mistake "no subcommand" for a
    /// subcommand that succeeded — which is the whole of the decision this makes.
    /// </remarks>
    public static int? Run(string[] args)
    {
        if (ExtensionCommandLine.Matches(args)) return ExtensionCommand.Run(args);
        if (BacktestCommandLine.Matches(args)) return BacktestCommand.Run(args);

        return null;
    }
}
