using System;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Infrastructure.Plugins;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Where a plugin's log lines land in the headless host: the operator's console.
/// </summary>
/// <remarks>
/// <see cref="PluginHostContext"/> takes a sink and defaults it to discarding everything, which is
/// the correct default for a type that cannot know who is listening — and the wrong outcome for a
/// host whose only user interface is this console. This is the sink that makes a plugin audible.
/// </remarks>
internal static class PluginConsole
{
    /// <summary>Writes one already-tagged plugin line.</summary>
    /// <remarks>
    /// Nothing is filtered by level. A line the host quietly dropped is indistinguishable from a
    /// plugin that had nothing to say. Warnings and errors go to stderr, matching where the host
    /// puts its own — an operator piping stdout to a log still sees them.
    /// <para>
    /// The <c>[plugin:&lt;id&gt;]</c> tag the context applies is left exactly as it arrives: it is
    /// the only thing distinguishing a plugin's claim from the host's own.
    /// </para>
    /// </remarks>
    internal static void WriteLogLine(string line, PluginLogLevel level)
    {
        if (level >= PluginLogLevel.Warning)
        {
            Console.Error.WriteLine(line);
            return;
        }

        Console.WriteLine(line);
    }
}
