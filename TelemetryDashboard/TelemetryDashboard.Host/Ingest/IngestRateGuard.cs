using System;
using System.Threading;
using TelemetryDashboard.Core.Resilience;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Stops one runaway channel from taking the console, the stream and the recorder down with it.
/// </summary>
/// <remarks>
/// The circuit breaker this wraps was written for M1 and then referenced by nothing, which meant
/// the documented protection did not exist: a firmware bug looping on its transmit buffer would
/// have driven the broadcast loop and the CSV writer at whatever rate the link could carry.
///
/// The guard drops data, and dropping measured data is the one thing this codebase will not do
/// quietly. So every isolation is announced on the console with the channel and the duration, the
/// dropped total is reported at shutdown, and the limit is far above any rate a serial link can
/// reach — a channel that trips it is not producing telemetry a chart could show anyway. The
/// breaker's own default of 50,000/s cannot be hit over a 115,200-baud line at all, which is
/// another way of saying it would have stayed inert even once it was wired up.
/// </remarks>
public sealed class IngestRateGuard
{
    /// <summary>Per-channel samples per second above which a channel is isolated.</summary>
    /// <remarks>
    /// A 115,200-baud link carries roughly 1,000 short frames a second at its absolute ceiling, so
    /// this leaves an order of magnitude of headroom over any real device and still catches a loop.
    /// </remarks>
    public const int DefaultMaxChannelRatePerSecond = 5_000;

    private readonly TelemetryCircuitBreaker? _breaker;
    private long _dropped;
    private long _isolations;

    /// <param name="maxChannelRatePerSecond">Zero or less disables the guard entirely.</param>
    public IngestRateGuard(int maxChannelRatePerSecond = DefaultMaxChannelRatePerSecond)
    {
        MaxChannelRatePerSecond = maxChannelRatePerSecond;
        if (maxChannelRatePerSecond <= 0) return;

        _breaker = new TelemetryCircuitBreaker
        {
            MaxAllowedRatePerSec = maxChannelRatePerSecond,
            IsolationDuration = TimeSpan.FromSeconds(1)
        };

        _breaker.ChannelIsolated += (_, channel) =>
        {
            Interlocked.Increment(ref _isolations);
            Console.Error.WriteLine(
                $"[ingest] channel '{channel}' exceeded {maxChannelRatePerSecond} samples/s and is " +
                $"isolated for {_breaker.IsolationDuration.TotalSeconds:0.#}s. Samples arriving on it " +
                "during that window are dropped and counted, not stored.");
        };

        _breaker.ChannelRestored += (_, channel) =>
            Console.Error.WriteLine($"[ingest] channel '{channel}' back under the limit; resumed.");
    }

    /// <summary>The configured limit; zero when the guard is disabled.</summary>
    public int MaxChannelRatePerSecond { get; }

    /// <summary>Whether the guard is doing anything at all.</summary>
    public bool IsActive => _breaker is not null;

    /// <summary>Samples refused so far.</summary>
    public long DroppedSamples => Interlocked.Read(ref _dropped);

    /// <summary>How many times a channel has been isolated.</summary>
    public long Isolations => Interlocked.Read(ref _isolations);

    /// <summary>Aggregate samples seen across all channels in the last second, or -1 when disabled.</summary>
    public int CurrentAggregateRate => _breaker?.CurrentAggregateRate ?? -1;

    /// <summary>
    /// Whether this sample may proceed. Counts the refusal when it may not.
    /// </summary>
    /// <remarks>
    /// The breaker's <c>AllowPacketProcessing</c> already records the sample against the channel's
    /// window, so calling <c>RecordPacket</c> beside it would count every sample twice and halve
    /// the effective limit.
    /// </remarks>
    public bool Allow(string channelId)
    {
        if (_breaker is null) return true;
        if (_breaker.AllowPacketProcessing(channelId)) return true;

        Interlocked.Increment(ref _dropped);
        return false;
    }

    /// <summary>One line for the shutdown report, or null when there is nothing to say.</summary>
    public string? Summary()
    {
        long dropped = DroppedSamples;
        if (dropped == 0) return null;

        return $"{dropped} samples dropped across {Isolations} channel isolations " +
               $"(limit {MaxChannelRatePerSecond}/s per channel).";
    }
}
