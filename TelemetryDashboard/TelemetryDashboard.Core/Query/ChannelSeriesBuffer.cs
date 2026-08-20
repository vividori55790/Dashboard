using System;

namespace TelemetryDashboard.Core.Query;

/// <summary>
/// Fixed-capacity rolling history for one channel, queryable by time window.
/// </summary>
/// <remarks>
/// <para>
/// Appends are O(1) and allocation-free; window extraction binary-searches the timestamp-ordered
/// ring rather than scanning it, so a 2,000-point query against a full buffer costs two searches
/// and one copy regardless of depth.
/// </para>
/// <para>
/// The buffer counts what it evicts. A caller that asks for sixty seconds of a channel whose ring
/// only held ten must be told that, or it will render ten seconds of data under a sixty-second
/// axis and read the gap as an outage.
/// </para>
/// </remarks>
public sealed class ChannelSeriesBuffer
{
    private readonly SeriesPoint[] _points;
    private readonly object _lock = new();
    private int _head;
    private int _count;
    private long _evicted;
    private long _outOfOrder;
    private double _lastTimestampSec = double.NegativeInfinity;

    public ChannelSeriesBuffer(int capacity = 4096)
    {
        if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity), "A series needs room for at least two samples.");
        _points = new SeriesPoint[capacity];
    }

    public int Capacity => _points.Length;

    public int Count { get { lock (_lock) return _count; } }

    /// <summary>Samples overwritten by newer ones. Non-zero means history has been lost.</summary>
    public long EvictedSampleCount { get { lock (_lock) return _evicted; } }

    /// <summary>
    /// Samples that arrived stamped earlier than their predecessor.
    /// </summary>
    /// <remarks>
    /// They are kept in arrival order rather than dropped or re-sorted. A non-zero count means the
    /// window search may include or exclude a sample at a boundary, and is surfaced instead of
    /// being quietly tolerated.
    /// </remarks>
    public long OutOfOrderArrivals { get { lock (_lock) return _outOfOrder; } }

    /// <summary>Appends one sample, evicting the oldest when full.</summary>
    public void Append(double timestampSec, double value)
    {
        lock (_lock)
        {
            if (timestampSec < _lastTimestampSec) _outOfOrder++;
            _lastTimestampSec = timestampSec;

            _points[_head] = new SeriesPoint(timestampSec, value);
            _head = (_head + 1) % _points.Length;

            if (_count == _points.Length) _evicted++;
            else _count++;
        }
    }

    /// <summary>Timestamp of the oldest retained sample, or <c>null</c> when empty.</summary>
    public double? OldestTimestampSec
    {
        get { lock (_lock) return _count == 0 ? null : At(0).TimestampSec; }
    }

    /// <summary>
    /// Copies the samples in <c>[startSec, endSec]</c> into <paramref name="destination"/>.
    /// </summary>
    /// <returns>
    /// Samples in the window, which may exceed the number copied when the destination is shorter.
    /// A caller that receives more than it had room for has an incomplete window and must not
    /// present it as complete.
    /// </returns>
    /// <remarks>
    /// Copied as one or two contiguous runs rather than element by element. The obvious loop costs
    /// a division per sample to unwrap the ring, which at a million samples a window was most of
    /// the query's wall clock — more than the reduction it was feeding.
    /// </remarks>
    public int CopyWindow(double startSec, double endSec, Span<SeriesPoint> destination)
    {
        lock (_lock)
        {
            int first = LowerBound(startSec);
            int last = UpperBound(endSec);
            int available = last - first;
            if (available <= 0) return 0;

            int copied = Math.Min(available, destination.Length);
            if (copied <= 0) return available;

            int physical = (_head - _count + first + _points.Length) % _points.Length;
            int firstRun = Math.Min(copied, _points.Length - physical);

            _points.AsSpan(physical, firstRun).CopyTo(destination);
            if (copied > firstRun) _points.AsSpan(0, copied - firstRun).CopyTo(destination[firstRun..]);

            return available;
        }
    }

    /// <summary>Samples in <c>[startSec, endSec]</c> without copying them.</summary>
    public int CountInWindow(double startSec, double endSec)
    {
        lock (_lock) return Math.Max(0, UpperBound(endSec) - LowerBound(startSec));
    }

    /// <summary>True when the oldest retained sample starts after the requested window did.</summary>
    public bool RetentionTruncates(double startSec)
    {
        lock (_lock) return _count > 0 && _evicted > 0 && At(0).TimestampSec > startSec;
    }

    private SeriesPoint At(int logicalIndex) =>
        _points[(_head - _count + logicalIndex + _points.Length) % _points.Length];

    /// <summary>First index whose timestamp is at least <paramref name="timestampSec"/>.</summary>
    private int LowerBound(double timestampSec)
    {
        int low = 0;
        int high = _count;
        while (low < high)
        {
            int mid = low + ((high - low) >> 1);
            if (At(mid).TimestampSec < timestampSec) low = mid + 1;
            else high = mid;
        }
        return low;
    }

    /// <summary>First index whose timestamp is greater than <paramref name="timestampSec"/>.</summary>
    private int UpperBound(double timestampSec)
    {
        int low = 0;
        int high = _count;
        while (low < high)
        {
            int mid = low + ((high - low) >> 1);
            if (At(mid).TimestampSec <= timestampSec) low = mid + 1;
            else high = mid;
        }
        return low;
    }
}
