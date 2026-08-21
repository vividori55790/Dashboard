using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Query;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Publishes each declared expression as a channel of its own, on the live ingest path.
/// </summary>
/// <remarks>
/// <c>/api/computed</c> answers a question; this makes the answer a channel. Once a computed value
/// goes through the same publisher as a measured one it is scored, broadcast, recorded, archived
/// and available to the spectrum and the DVR — so an operator can put an efficiency on a chart
/// beside the voltages it came from, and an alert can fire on it.
/// <para>
/// <b>The instant is chosen, not configured.</b> Evaluating at "now" would refuse everything: an
/// input other than the one that just arrived has no sample after now, so it could only be held,
/// and a held value describes a different moment. The instant used here is the <em>oldest of the
/// inputs' newest samples</em>. At that instant every input has something at or after it, so every
/// input is measured there or interpolated between two samples that bracket it, and none is
/// extrapolated.
/// </para>
/// <para>
/// That choice also sets the rate, correctly and without a setting. The instant only advances when
/// the slowest input advances, so a 10 Hz voltage and a 1 Hz current produce a 1 Hz power rather
/// than ten interpolations a second of the same two current samples. A derived channel published
/// faster than its slowest input is manufactured detail, and it looks like resolution.
/// </para>
/// <para>
/// An input that falls silent stops the derived channel, which is the truthful outcome: an
/// efficiency whose current sensor has stopped is unknown, not the efficiency it had when the
/// sensor was last heard from.
/// </para>
/// </remarks>
public sealed class ComputedChannelPump : IComputedChannelCounters
{
    /// <summary>Node id derived channels are published under.</summary>
    /// <remarks>
    /// Its own node rather than the reporting device's, because no device reported it. A viewer
    /// grouping by node then sees derived quantities separately from measurements without having
    /// to know which names are which.
    /// </remarks>
    public const string NodeId = "computed";

    /// <summary>How often the pump looks for a new instant to compute.</summary>
    /// <remarks>
    /// This is a ceiling on the output rate as well as on latency, which an earlier version of
    /// this comment denied: it claimed publication was driven purely by the data. Measured against
    /// a live host, inputs arriving at 9 Hz produced a derived channel at exactly 5.00 Hz — the
    /// tick rate — so the published rate is the lower of the slowest input's rate and this.
    /// <para>
    /// Five samples a second of a derived quantity is ample, and the alternative costs an
    /// alignment pass over every input on every tick. What matters is that the cap only ever
    /// removes samples: each one published is still a real computation at an instant every input
    /// answered.
    /// </para>
    /// </remarks>
    public const int TickMilliseconds = 200;

    /// <summary>History the alignment is allowed to look at around the instant.</summary>
    /// <remarks>
    /// Much shorter than the endpoint's default, because the instant here is chosen so that every
    /// input already has a sample at or after it: the alignment only has to find the pair that
    /// brackets it. The endpoint's 30 seconds is for an arbitrary instant a caller names, and
    /// copying that much of every input on every tick is work for samples nothing reads.
    /// </remarks>
    public const double WindowSec = 10.0;

    private readonly TelemetryStreamingServer _server;
    private readonly Func<TelemetryPacket, string, CancellationToken, ValueTask> _publish;
    private readonly Dictionary<string, double> _lastInstant = new(StringComparer.Ordinal);

    private readonly HashSet<string> _broken = new(StringComparer.Ordinal);

    private long _published;
    private long _withheld;
    private long _faulted;
    private int _reported;

    public ComputedChannelPump(
        TelemetryStreamingServer server,
        Func<TelemetryPacket, string, CancellationToken, ValueTask> publish)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));

        // So /api/status can say what this pump has done. Registered here rather than by the
        // caller, because a pump nobody can see the counters of is the situation this replaces.
        _server.ComputedCounters = this;
    }

    /// <summary>Derived samples that reached the publisher.</summary>
    public long Published => Interlocked.Read(ref _published);

    /// <summary>
    /// Instants that existed but could not be computed, because an input did not answer them.
    /// </summary>
    /// <remarks>
    /// Counted rather than logged. A derived channel that is quiet because its inputs disagree
    /// about time looks exactly like one nobody declared, and the difference is what an operator
    /// needs in order to go and look at the sensor.
    /// </remarks>
    public long Withheld => Interlocked.Read(ref _withheld);

    /// <summary>Runs until cancelled.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var ticks = new PeriodicTimer(TimeSpan.FromMilliseconds(TickMilliseconds));

        try
        {
            while (await ticks.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await TickAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a fault.
        }
    }

    /// <summary>One pass over the declared channels. Exposed so a test can drive it directly.</summary>
    public async ValueTask TickAsync(CancellationToken cancellationToken = default)
    {
        // Read each tick rather than captured, so a channel declared after this pump started is
        // picked up, and so the order is the declaration order: a channel that reads another
        // computed channel sees the value published a moment ago in this same pass.
        foreach (ComputedChannel channel in _server.Computed)
        {
            try
            {
                await PublishOneAsync(channel, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per channel, and said the first time it happens. Written first as one try around
                // the whole loop with the task awaited only at shutdown -- so this pump published
                // for a while, threw, and went quiet, and every surface showed a derived channel
                // that had simply stopped arriving. A silent stop is the failure this codebase
                // spends most of its effort refusing, and it was in the thing built to avoid it.
                Interlocked.Increment(ref _faulted);
                _broken.Add(channel.Id);

                if (Interlocked.Exchange(ref _reported, 1) == 0)
                {
                    FaultMessage = $"{channel.Id}: {ex.GetType().Name}: {ex.Message}";
                    Console.Error.WriteLine($"[computed] {FaultMessage}");
                    Console.Error.WriteLine("[computed] that channel is skipped; the others keep running.");
                }
            }
        }
    }

    /// <summary>Channels that threw, and so are no longer attempted.</summary>
    public long Faulted => Interlocked.Read(ref _faulted);

    /// <summary>The first fault seen, or null when none has been.</summary>
    public string? FaultMessage { get; private set; }

    private async ValueTask PublishOneAsync(ComputedChannel channel, CancellationToken cancellationToken)
    {
        if (_broken.Contains(channel.Id)) return;

        var keys = new List<string>(channel.Inputs.Count);
        double instant = double.MaxValue;

        foreach (string input in channel.Inputs)
        {
            ComputedInputResolver.Resolution resolved = ComputedInputResolver.Resolve(_server.Series, input);
            if (resolved.Key is null) return;

            ChannelSeriesBuffer? series = _server.Series.Find(resolved.Key);
            if (series?.NewestTimestampSec is not { } newest) return;

            keys.Add(resolved.Key);
            instant = Math.Min(instant, newest);
        }

        if (keys.Count == 0) return;

        // Strictly after the last one published, so an instant is never emitted twice and a
        // stalled input stops the channel rather than repeating its last computable moment.
        if (_lastInstant.TryGetValue(channel.Id, out double previous) && instant <= previous) return;

        AlignedEndpoint.Result aligned =
            AlignedEndpoint.Compute(_server.Series, keys, instant, WindowSec);

        if (!aligned.Channels.All(c => c.AnswersTheInstant))
        {
            Interlocked.Increment(ref _withheld);
            return;
        }

        var byName = new Dictionary<string, double?>(StringComparer.Ordinal);
        for (int i = 0; i < keys.Count; i++) byName[channel.Inputs[i]] = aligned.Channels[i].Value;

        if (channel.Evaluate(id => byName.TryGetValue(id, out double? v) ? v : null) is not { } value)
        {
            Interlocked.Increment(ref _withheld);
            return;
        }

        _lastInstant[channel.Id] = instant;
        Interlocked.Increment(ref _published);

        await _publish(
            new TelemetryPacket
            {
                NodeId = NodeId,
                Variable = channel.Id,
                Value = value,
                Unit = channel.Unit,
                Timestamp = DateTime.UnixEpoch.AddSeconds(instant),
                Flags = PacketFlags.IsDerived
            },
            NodeId,
            cancellationToken).ConfigureAwait(false);
    }
}
