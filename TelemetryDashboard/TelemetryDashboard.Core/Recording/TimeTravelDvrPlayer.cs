using System;
using System.Collections.Generic;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Recording;

/// <summary>Carries the frame surfaced by a DVR scrub or playback step.</summary>
public sealed class DvrFrameEventArgs : EventArgs
{
    public DvrFrameEventArgs(DvrFrame frame, double scrubTimeSec)
    {
        Frame = frame;
        ScrubTimeSec = scrubTimeSec;
    }

    public DvrFrame Frame { get; }

    /// <summary>Absolute timeline position, in seconds, that produced this frame.</summary>
    public double ScrubTimeSec { get; }
}

/// <summary>
/// Time-travel DVR: records telemetry frames on a rolling timeline and replays any moment at
/// 0.1 second scrub precision.
/// </summary>
/// <remarks>
/// Frames live in a pre-allocated ring buffer. The previous implementation evicted with
/// <c>List.RemoveAt(0)</c>, an O(n) shift executed once per frame — at the documented 100,000
/// frame depth that is five billion element moves per full buffer cycle, which stalled ingest
/// exactly when an incident was generating the most data. Range queries binary-search the
/// timestamp-ordered buffer instead of scanning it.
/// </remarks>
public class TimeTravelDvrPlayer
{
    /// <summary>Scrub resolution mandated by the specification.</summary>
    public const double ScrubPrecisionSec = 0.1;

    private readonly DvrFrame[] _frames;
    private readonly object _lock = new();
    private int _head;
    private int _count;

    public TimeTravelDvrPlayer(int capacity = 100_000)
    {
        if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity), "DVR must retain at least two frames.");
        _frames = new DvrFrame[capacity];
    }

    public int Capacity => _frames.Length;

    public bool IsPlaying { get; private set; }

    /// <summary>Playback rate multiplier applied by <see cref="Advance"/>.</summary>
    public double PlaybackSpeed { get; set; } = 1.0;

    public double CurrentScrubTimeSec { get; private set; }

    public int FrameCount
    {
        get { lock (_lock) return _count; }
    }

    /// <summary>Span, in seconds, between the oldest and newest retained frame.</summary>
    public double MaxDurationSec
    {
        get
        {
            lock (_lock)
            {
                return _count < 2 ? 0.0 : At(_count - 1).TimestampSec - At(0).TimestampSec;
            }
        }
    }

    public event EventHandler<DvrFrameEventArgs>? FrameReplayed;

    /// <summary>Records a frame stamped with the current UTC clock.</summary>
    public void RecordFrame(string channelName, double value, double zScore, bool isAnomaly, string? analyzerId = null) =>
        RecordFrame(channelName, value, zScore, isAnomaly, UtcNowSeconds(), analyzerId);

    /// <summary>Records a frame at an explicit timeline position.</summary>
    /// <param name="analyzerId">
    /// Identifies the analyzer behind <paramref name="zScore"/> and <paramref name="isAnomaly"/>.
    /// Leave null when nothing scored this sample: the frame is then replayed as unevaluated
    /// rather than as a confident verdict of zero.
    /// </param>
    public void RecordFrame(string channelName, double value, double zScore, bool isAnomaly, double timestampSec, string? analyzerId = null)
    {
        var frame = new DvrFrame
        {
            TimestampSec = timestampSec,
            ChannelName = channelName ?? string.Empty,
            Value = value,
            ZScore = zScore,
            IsAnomaly = isAnomaly,
            AnalyzerId = analyzerId
        };

        lock (_lock)
        {
            int tail = (_head + _count) % _frames.Length;
            _frames[tail] = frame;

            if (_count < _frames.Length)
            {
                _count++;
            }
            else
            {
                _head = (_head + 1) % _frames.Length; // drop the oldest frame in O(1)
            }
        }
    }

    /// <summary>Frames within an absolute time range, inclusive of both bounds.</summary>
    public List<DvrFrame> GetFramesInRange(double startTimeSec, double endTimeSec)
    {
        lock (_lock)
        {
            var result = new List<DvrFrame>();
            if (_count == 0 || endTimeSec < startTimeSec) return result;

            for (int i = LowerBound(startTimeSec); i < _count; i++)
            {
                DvrFrame frame = At(i);
                if (frame.TimestampSec > endTimeSec) break;
                result.Add(frame);
            }
            return result;
        }
    }

    /// <summary>Frames within <paramref name="windowWidthSec"/> centred on a moment.</summary>
    public List<DvrFrame> ExtractSnapshot(double centerTimeSec, double windowWidthSec = 30.0)
    {
        double halfWindow = Math.Abs(windowWidthSec) / 2.0;
        return GetFramesInRange(centerTimeSec - halfWindow, centerTimeSec + halfWindow);
    }

    /// <summary>Timeline start, in seconds, or 0 when nothing is recorded.</summary>
    public double TimelineStartSec
    {
        get { lock (_lock) return _count == 0 ? 0.0 : At(0).TimestampSec; }
    }

    public void Play() => IsPlaying = true;

    public void Pause() => IsPlaying = false;

    /// <summary>
    /// Advances playback by <paramref name="elapsedSec"/> of wall time scaled by
    /// <see cref="PlaybackSpeed"/>, emitting the frame now under the playhead.
    /// </summary>
    public void Advance(double elapsedSec)
    {
        if (!IsPlaying || elapsedSec <= 0) return;

        double target = CurrentScrubTimeSec - TimelineStartSec + elapsedSec * PlaybackSpeed;
        if (target >= MaxDurationSec)
        {
            target = MaxDurationSec;
            IsPlaying = false; // reached the live edge
        }
        ScrubToRelative(target);
    }

    /// <summary>Scrubs to an offset measured from the start of the retained timeline.</summary>
    public void ScrubToRelative(double relativeTimeSec)
    {
        lock (_lock)
        {
            if (_count == 0) return;
            double clamped = Math.Clamp(relativeTimeSec, 0.0, MaxDurationSec);
            EmitFrameAt(At(0).TimestampSec + Quantize(clamped));
        }
    }

    /// <summary>Scrubs to an absolute timeline position.</summary>
    public void ScrubTo(double scrubTimeSec)
    {
        lock (_lock)
        {
            if (_count == 0) return;
            EmitFrameAt(Quantize(scrubTimeSec));
        }
    }

    /// <summary>Alias retained for existing callers; equivalent to <see cref="ScrubToRelative"/>.</summary>
    public void SeekTo(double relativeTimestampSec) => ScrubToRelative(relativeTimestampSec);

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_frames, 0, _frames.Length);
            _head = 0;
            _count = 0;
            CurrentScrubTimeSec = 0.0;
            IsPlaying = false;
        }
    }

    internal static double UtcNowSeconds() => DateTime.UtcNow.Ticks / 10_000_000.0;

    /// <summary>Snaps a position to the 0.1 second scrub grid.</summary>
    private static double Quantize(double timeSec) =>
        Math.Round(timeSec / ScrubPrecisionSec, MidpointRounding.AwayFromZero) * ScrubPrecisionSec;

    /// <summary>Caller must hold the lock.</summary>
    private void EmitFrameAt(double absoluteTimeSec)
    {
        CurrentScrubTimeSec = absoluteTimeSec;

        int index = LowerBound(absoluteTimeSec);
        DvrFrame frame = index >= _count ? At(_count - 1) : At(index);

        // Prefer whichever neighbour actually sits closest to the requested moment.
        if (index > 0 && index < _count)
        {
            DvrFrame previous = At(index - 1);
            if (Math.Abs(previous.TimestampSec - absoluteTimeSec) <= Math.Abs(frame.TimestampSec - absoluteTimeSec))
            {
                frame = previous;
            }
        }

        FrameReplayed?.Invoke(this, new DvrFrameEventArgs(frame, absoluteTimeSec));
    }

    /// <summary>Index of the first frame at or after <paramref name="timeSec"/>. Caller must hold the lock.</summary>
    private int LowerBound(double timeSec)
    {
        int low = 0;
        int high = _count;

        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (At(mid).TimestampSec < timeSec) low = mid + 1;
            else high = mid;
        }
        return low;
    }

    /// <summary>The i-th oldest frame. Caller must hold the lock.</summary>
    private DvrFrame At(int index) => _frames[(_head + index) % _frames.Length];
}
