using System;
using System.Collections.Generic;
using System.IO;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Recording;

namespace TelemetryDashboard.Infrastructure.Replay;

/// <summary>
/// Time-machine playback of a recorded session: loads a CSV produced by
/// <c>TelemetryCsvRecorder</c> and exposes a scrubbable timeline over it.
/// </summary>
/// <remarks>
/// The recorder writes an absolute <c>Timestamp_Sec</c> (ticks since year one), so only the
/// differences between rows carry meaning. Frames are therefore rebased onto a timeline that starts
/// at zero — a seek bar labelled with 63.9 billion is useless, and every clamp below would have to
/// carry an offset.
/// <para>
/// A row that cannot be read is skipped rather than aborting the load. Recordings are frequently cut
/// short by the power event they were capturing, which leaves a torn final line; refusing to open the
/// file would discard the evidence at exactly the moment it is wanted.
/// </para>
/// </remarks>
public sealed class SessionReplayPlayer
{
    private readonly List<DvrFrame> _frames = new();

    /// <summary>Path of the loaded recording, or empty when nothing is loaded.</summary>
    public string SessionPath { get; private set; } = string.Empty;

    /// <summary>Frames of the loaded session, oldest first, timestamped from zero.</summary>
    public IReadOnlyList<DvrFrame> Frames => _frames;

    /// <summary>Length of the timeline in seconds; zero for an empty recording.</summary>
    public double TotalDurationSeconds { get; private set; }

    /// <summary>Current scrub position, always within <c>[0, TotalDurationSeconds]</c>.</summary>
    public double CurrentPositionSeconds { get; private set; }

    /// <summary>Playback rate multiplier; always positive and finite.</summary>
    public double PlaybackSpeed { get; private set; } = 1.0;

    /// <summary>
    /// Loads a recorded session, replacing anything previously loaded.
    /// </summary>
    /// <exception cref="ArgumentException">The path is empty.</exception>
    /// <exception cref="FileNotFoundException">The recording does not exist.</exception>
    public void LoadSession(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A session file path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The recorded session file was not found.", path);
        }

        _frames.Clear();
        SessionPath = path;

        var absoluteSeconds = new List<double>();
        foreach (string line in File.ReadLines(path))
        {
            if (SessionCsvRowParser.TryParse(line, out DvrFrame? frame, out double timestampSec) && frame is not null)
            {
                _frames.Add(frame);
                absoluteSeconds.Add(timestampSec);
            }
        }

        Rebase(absoluteSeconds);

        // A fresh recording always opens at its start; carrying a stale cursor over would point into
        // a session that no longer exists.
        CurrentPositionSeconds = 0.0;
    }

    /// <summary>
    /// Overrides the timeline length, for hosts that know the duration without parsing a file.
    /// Negative values collapse to zero.
    /// </summary>
    public void SetDuration(double seconds)
    {
        TotalDurationSeconds = double.IsFinite(seconds) && seconds > 0 ? seconds : 0.0;
        Seek(CurrentPositionSeconds);
    }

    /// <summary>
    /// Sets the playback rate. Zero, negative, and non-finite values fall back to 1x.
    /// </summary>
    /// <remarks>
    /// A zero multiplier would stall the timeline while still reporting that playback is running,
    /// and a negative one would drive the cursor off the start of the session; neither is what an
    /// operator dragging a speed control intends, so both resolve to real-time.
    /// </remarks>
    public void SetSpeed(double speed)
    {
        PlaybackSpeed = double.IsFinite(speed) && speed > 0 ? speed : 1.0;
    }

    /// <summary>Moves the cursor, clamped to the loaded timeline.</summary>
    public void Seek(double seconds)
    {
        if (double.IsNaN(seconds))
        {
            CurrentPositionSeconds = 0.0;
            return;
        }

        CurrentPositionSeconds = Math.Clamp(seconds, 0.0, TotalDurationSeconds);
    }

    /// <summary>Shifts every frame so the session starts at t=0 and derives the total length.</summary>
    private void Rebase(List<double> absoluteSeconds)
    {
        if (absoluteSeconds.Count == 0)
        {
            TotalDurationSeconds = 0.0;
            return;
        }

        double first = absoluteSeconds[0];
        double last = first;

        for (int i = 0; i < _frames.Count; i++)
        {
            double relative = absoluteSeconds[i] - first;
            _frames[i].TimestampSec = relative;
            if (absoluteSeconds[i] > last) last = absoluteSeconds[i];
        }

        TotalDurationSeconds = last - first;
    }
}
