using System;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Deciding whether a sender's timestamp is a clock reading at all.
/// </summary>
/// <remarks>
/// Split from the recognising half because it is the only part here that judges rather than reads,
/// and the judgement is the delicate one: too strict and the fault worth finding is discarded
/// along with the noise.
/// </remarks>
public static partial class PeerFrameParser
{
    /// <summary>
    /// How far from this host's clock a sender's timestamp may be and still be read as one.
    /// </summary>
    /// <remarks>
    /// Wide on purpose. A clock offset is the thing worth measuring, so a peer whose timezone is
    /// wrong by fourteen hours has to be accepted — refusing it would discard exactly the
    /// observation that reveals the fault. What this excludes is a value that is not a clock
    /// reading at all: an unset RTC reporting 1970 or 2000, or a field left at its default. Beyond
    /// a year that is no longer skew, and §7 says input from the network is not more trustworthy
    /// than input from a serial cable, which this codebase already drops rather than scrapes.
    /// </remarks>
    public static readonly TimeSpan PlausibleClockWindow = TimeSpan.FromDays(365);

    /// <summary>The sender's own clock reading, or null when it did not send a usable one.</summary>
    private static DateTime? ReadClock(string? sent, DateTime receivedUtc)
    {
        if (string.IsNullOrWhiteSpace(sent)) return null;

        if (!DateTime.TryParse(sent, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal
                | System.Globalization.DateTimeStyles.AssumeUniversal,
                out DateTime observed))
        {
            return null;
        }

        // Refused rather than clamped. A clamped timestamp is a number nobody reported, and it
        // would go on to be differenced against this host's clock and reported as an offset.
        return (observed - receivedUtc).Duration() <= PlausibleClockWindow ? observed : null;
    }
}
