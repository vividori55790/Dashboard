using System;
using System.Threading.Tasks;

namespace TelemetryDashboard.Host.Startup;

/// <summary>Ending a run that cannot start, without leaving its port held.</summary>
public static class StartupRefusal
{
    /// <summary>
    /// Says why the run cannot start, closes the listener, and ends.
    /// </summary>
    /// <remarks>
    /// The listener is already bound by the time any of these checks run, so every refusal has to
    /// close it. Written once because it was written four times, and a fifth check that forgot the
    /// close would leave a port held by a process that had already decided not to serve.
    /// </remarks>
    public static async Task<int> EndAsync(WebConsoleHost console, string message)
    {
        Console.Error.WriteLine($"telemetry-host: {message}");
        await console.DisposeAsync().ConfigureAwait(false);
        return Program.ExitUsage;
    }
}
