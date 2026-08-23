using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// The listening loop, and the one thing it shows while it listens.
/// </summary>
/// <remarks>
/// Separate from the command because it is the only part with a shape of its own: everything else
/// is decisions taken before or after. It routes each line through the rules the serving host would
/// use and hands both the line and whatever came of it to the survey, which is what lets a run
/// report "228 lines arrived and nothing claimed any of them" rather than reporting nothing.
/// </remarks>
internal static class SniffListener
{
    public static async Task ListenAsync(
        ITelemetrySource source, DataRouter router, WireSurvey survey,
        TimeSpan duration, CancellationToken cancellationToken)
    {
        DateTime started = DateTime.UtcNow;
        DateTime lastTick = started;

        try
        {
            await foreach (RawPacket raw in source.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                survey.Observe(raw, router.Route(raw).ToList());

                // Fifteen seconds of an unmoving cursor is indistinguishable from a hang, and the
                // count moving is also the first evidence that the device is talking at all.
                if (!Console.IsOutputRedirected && DateTime.UtcNow - lastTick > TimeSpan.FromSeconds(1))
                {
                    lastTick = DateTime.UtcNow;
                    Console.Write(
                        $"\r  {(lastTick - started).TotalSeconds:0.#}s / {duration.TotalSeconds:0.#}s — "
                        + $"{survey.Lines:N0} line(s), {survey.Channels.Count} channel(s)   ");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The listening window closed, which is how this command always ends.
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\r[sniff] source stopped: {ex.GetType().Name}: {ex.Message}");
        }

        if (!Console.IsOutputRedirected) Console.Write("\r" + new string(' ', 72) + "\r");
    }
}
