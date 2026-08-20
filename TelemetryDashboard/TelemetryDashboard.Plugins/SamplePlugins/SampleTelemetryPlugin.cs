namespace TelemetryDashboard.Plugins.SamplePlugins;

using System.Collections.Concurrent;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

/// <summary>
/// A worked example of an extension that does something: it derives a rate-of-change channel from
/// the telemetry the host is already routing, and persists it through the host's data logger.
/// </summary>
/// <remarks>
/// This plugin used to have an empty <see cref="OnPacketReceived"/> and a comment saying "sample
/// plugin packet processing hook". It loaded, it logged that it had initialised, and it did
/// nothing — which made it a demonstration that the extension surface existed rather than that it
/// worked. Everything below is computed from packets the host actually delivered; nothing is
/// synthesised, and a channel that never arrives produces no derived samples.
/// <para>
/// The derived value is <c>(v₂ − v₁) / (t₂ − t₁)</c> in units per second, from two consecutive real
/// samples of the same node and variable. The first sample of a channel yields nothing, because one
/// point has no rate — reporting a zero there would be an invented reading.
/// </para>
/// </remarks>
public class SampleTelemetryPlugin : IPlugin
{
    /// <summary>Unchanged from the inert version of this plugin: existing deployments key on it.</summary>
    public string Id => "sample.plugin";
    public string Name => "Sample Telemetry Plugin";
    public string Version => "1.1.0";

    /// <summary>Derived samples buffered before a batch write, keeping the ingest thread moving.</summary>
    private const int FlushEvery = 200;

    /// <summary>Suffix appended to a variable name to form its derived channel.</summary>
    private const string DerivedSuffix = ".rate";

    private readonly ConcurrentDictionary<string, (DateTime At, double Value)> _previous = new();
    private readonly List<TelemetryPacket> _pending = new();
    private IPluginContext? _context;
    private long _derived;
    private long _persisted;

    public void Initialize(IPluginContext context)
    {
        _context = context;
        _context.Log($"deriving '<variable>{DerivedSuffix}' in units/second from every routed packet; "
            + $"writing to the host data logger every {FlushEvery} derived samples.");
    }

    /// <summary>
    /// Derives a rate for this channel from the previous sample of the same channel.
    /// </summary>
    /// <remarks>
    /// Runs on the ingest thread for every routed packet, so it does no I/O: derived samples go
    /// into a buffer and are written in batches. Blocking here would slow the pump that feeds every
    /// other consumer of the stream.
    /// </remarks>
    public void OnPacketReceived(TelemetryPacket packet)
    {
        if (packet is null || packet.Variable.EndsWith(DerivedSuffix, StringComparison.Ordinal)) return;

        string channel = $"{packet.NodeId}/{packet.Variable}";
        (DateTime At, double Value) current = (packet.Timestamp, packet.Value);

        if (_previous.TryGetValue(channel, out (DateTime At, double Value) previous))
        {
            double seconds = (current.At - previous.At).TotalSeconds;

            // A non-advancing or reordered timestamp gives no defensible rate. Skipped rather than
            // clamped: a fabricated denominator would produce a number that looks measured.
            if (seconds > 0) Record(packet, (current.Value - previous.Value) / seconds);
        }

        _previous[channel] = current;
    }

    /// <summary>This plugin parses no wire format; it consumes what the router already decoded.</summary>
    public bool TryCustomParse(RawPacket rawPacket, out IEnumerable<TelemetryPacket> parsedPackets)
    {
        parsedPackets = Enumerable.Empty<TelemetryPacket>();
        return false;
    }

    /// <summary>Flushes what is buffered and reports what was actually written.</summary>
    /// <remarks>
    /// The count is the number of rows the logger accepted, not the number derived. Reporting the
    /// latter would claim a persistence that a failing write never performed.
    /// </remarks>
    public void Shutdown()
    {
        Flush();
        _context?.Log($"shutting down: {_derived} derived samples, {_persisted} written to the data logger.");
        _context = null;
    }

    private void Record(TelemetryPacket source, double rate)
    {
        var derived = new TelemetryPacket(
            source.NodeId,
            source.Variable + DerivedSuffix,
            rate,
            string.IsNullOrWhiteSpace(source.Unit) ? "/s" : source.Unit + "/s",
            source.Timestamp,
            source.Flags | PacketFlags.IsDerived);

        int pending;
        lock (_pending)
        {
            _pending.Add(derived);
            pending = _pending.Count;
        }

        if (Interlocked.Increment(ref _derived) == 1)
        {
            _context?.Log($"first derived sample: {derived.NodeId}/{derived.Variable} "
                + $"= {derived.Value:0.###} {derived.Unit}");
        }

        if (pending >= FlushEvery) Flush();
    }

    /// <summary>
    /// Writes the buffered derived samples.
    /// </summary>
    /// <remarks>
    /// A failed write is reported and the batch is dropped rather than retried forever: this is a
    /// derived convenience channel, and letting it grow without bound would cost the host memory it
    /// needs for the measured data.
    /// </remarks>
    private void Flush()
    {
        List<TelemetryPacket> batch;
        lock (_pending)
        {
            if (_pending.Count == 0) return;
            batch = new List<TelemetryPacket>(_pending);
            _pending.Clear();
        }

        try
        {
            _context?.Logger.WriteBatchAsync(batch).GetAwaiter().GetResult();
            Interlocked.Add(ref _persisted, batch.Count);
        }
        catch (Exception ex)
        {
            _context?.Log($"could not persist {batch.Count} derived samples: {ex.Message}", PluginLogLevel.Error);
        }
    }
}
