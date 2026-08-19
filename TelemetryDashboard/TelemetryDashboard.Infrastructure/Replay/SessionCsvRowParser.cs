using System;
using System.Globalization;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Replay;

/// <summary>
/// Reads one row of the recorder's CSV layout
/// (<c>Timestamp_ISO,Timestamp_Sec,NodeId,Channel,Value,ZScore,IsAnomaly,Predicted_60s,Status</c>).
/// </summary>
/// <remarks>
/// The recorder writes unquoted invariant-culture fields, so a plain split matches exactly what it
/// produces and nothing more elaborate is warranted. Every failure is reported as <c>false</c>: a
/// replay must survive the torn last line of a recording that was cut short.
/// </remarks>
internal static class SessionCsvRowParser
{
    /// <summary>Column count below which a row cannot be placed on the timeline at all.</summary>
    private const int MinimumFields = 7;

    private const string HeaderPrefix = "Timestamp_ISO";

    /// <summary>Parses a data row, reporting its absolute timestamp separately from the frame.</summary>
    /// <param name="timestampSec">
    /// Seconds on the recorder's absolute scale (ticks since year one). Rebasing is the caller's job,
    /// because only it knows where the session starts.
    /// </param>
    internal static bool TryParse(string line, out DvrFrame? frame, out double timestampSec)
    {
        frame = null;
        timestampSec = 0.0;

        if (string.IsNullOrWhiteSpace(line)) return false;
        if (line.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase)) return false;

        string[] fields = line.Split(',');
        if (fields.Length < MinimumFields) return false;

        if (!TryReadTimestamp(fields, out timestampSec)) return false;
        if (!TryReadDouble(fields[4], out double value)) return false;

        // A missing z-score is not worth discarding a sample over; the measurement is the payload.
        bool scored = TryReadDouble(fields[5], out double zScore);

        frame = new DvrFrame
        {
            ChannelName = fields[3],
            Value = value,
            ZScore = zScore,
            IsAnomaly = string.Equals(fields[6].Trim(), "TRUE", StringComparison.OrdinalIgnoreCase),

            // A recorded score is a real verdict — something computed it — but the CSV layout has no
            // column naming the analyzer, so the reading can be honoured without being reproducible.
            // A row whose score column is absent or unreadable gets no analyzer at all: replaying it
            // as a confident 0.0σ would manufacture the judgement the recording failed to preserve.
            AnalyzerId = scored ? DvrFrame.UnidentifiedAnalyzer : null
        };

        return true;
    }

    /// <summary>
    /// Prefers the numeric seconds column and falls back to the ISO timestamp, converting it with the
    /// recorder's own tick scale so both columns land on one timeline.
    /// </summary>
    private static bool TryReadTimestamp(string[] fields, out double timestampSec)
    {
        if (TryReadDouble(fields[1], out timestampSec)) return true;

        if (DateTime.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime iso))
        {
            timestampSec = iso.Ticks / 10_000_000.0;
            return true;
        }

        return false;
    }

    private static bool TryReadDouble(string field, out double value) =>
        double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
