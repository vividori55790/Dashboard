using System;

namespace TelemetryDashboard.Core.Query;

/// <summary>
/// The single time base for everything in the query and streaming path: seconds since the Unix
/// epoch, UTC.
/// </summary>
/// <remarks>
/// Stated in one place because two clocks in one pipeline is how a chart ends up plotting a
/// window that does not exist. The DVR timeline counts from year one (.NET ticks) and the browser
/// counts from 1970; a series that mixed them would silently place every point 62 years away.
/// Anything that stamps or queries a <see cref="SeriesPoint"/> uses this.
/// </remarks>
public static class SeriesClock
{
    /// <summary>Now, in seconds since the Unix epoch.</summary>
    public static double UtcNowSec() =>
        (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

    /// <summary>Converts an absolute instant to the series time base.</summary>
    public static double ToSeconds(DateTime utc) =>
        (utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime()).Subtract(DateTime.UnixEpoch).TotalSeconds;
}
