using System;
using System.Text.Json;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>
/// Reads a frame this product emitted, when one arrives back as input.
/// </summary>
/// <remarks>
/// Point a host at another host's <c>/stream</c> and the frames it receives are
/// <see cref="TelemetryFrame"/>s — its own output shape. Nothing recognised them, so they fell
/// through to <see cref="RawPayloadParser"/>, whose contract is to emit one channel per numeric
/// property of an unknown object. Measured against a live pair of hosts, that produced:
/// <list type="bullet">
/// <item><description>
/// every channel collapsed into one called <c>value</c>. <c>ambient.temperature</c>,
/// <c>ambient.humidity</c> and <c>machine.vibration</c> all became the same series, alternating
/// between vibration in g and a figure near 1000 — ARCHITECTURE §2's "a series that looks like
/// noisy data and is actually two datasets interleaved", and §2 is right that nothing in the
/// numbers reveals it.
/// </description></item>
/// <item><description>
/// the sender's <em>verdicts</em> ingested as measurements. <c>anomalyScore</c> became a channel
/// with 1,292 samples and <c>predicted</c> one with 783, and the receiving host then scored them —
/// publishing an anomaly score of an anomaly score.
/// </description></item>
/// <item><description>
/// units dropped. <c>°C</c>, <c>%</c> and <c>g</c> all arrived as the empty string.
/// </description></item>
/// <item><description>
/// the connection housekeeping event <c>{"event":"connected","port":8074}</c> read as telemetry,
/// giving a channel named <c>port</c> holding the number 8074.
/// </description></item>
/// </list>
/// <para>
/// So this recognises the shape and emits the one sample it actually is. It deserialises into
/// <see cref="TelemetryFrame"/> — the same type the outbound path builds — rather than reading
/// field names of its own, so the reader and the writer cannot drift apart.
/// </para>
/// <para>
/// The verdict fields are deliberately <em>not</em> carried over. An anomaly score is the sending
/// host's judgement against the baseline <em>it</em> holds, and a limit breach is measured against
/// limits <em>it</em> was configured with; adopting either would let a peer's configuration decide
/// what this host considers alarming. Dropping them loses information that §7 says ought to be kept
/// attributed to the peer instead, and that is recorded as owed rather than half-built here.
/// </para>
/// </remarks>
public static partial class PeerFrameParser
{
    /// <summary>Frames that are not samples at all.</summary>
    /// <remarks>
    /// The stream opens with <c>{"event":"connected","port":N}</c>. It has no <c>variable</c>, so
    /// the check below already rejects it; naming the field is what makes that deliberate rather
    /// than lucky, because a housekeeping frame that gained a numeric field would otherwise start
    /// producing channels again.
    /// </remarks>
    public const string HousekeepingField = "event";

    /// <summary>Whether this payload is stream housekeeping rather than a reading.</summary>
    /// <remarks>
    /// Answered separately from <see cref="Parse"/> because the two mean different things to the
    /// caller. Parse returning null says "not a peer frame, try the next parser"; this says "a peer
    /// frame that is not a measurement, and no parser should have it". Folding them together is
    /// what produced a channel called <c>port</c> holding the number 8074: the connection event has
    /// no <c>variable</c>, so Parse declined it, and the last-resort parser then did what it is for.
    /// </remarks>
    public static bool IsHousekeeping(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty(HousekeepingField, out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The sample a peer frame carries, or null when this payload is not one.</summary>
    public static TelemetryPacket? Parse(RawPacket raw, string payload)
    {
        TelemetryFrame? frame;
        try
        {
            frame = JsonSerializer.Deserialize<TelemetryFrame>(payload);
        }
        catch (JsonException)
        {
            return null;
        }

        // The discriminator: this product names the channel and the reading separately. An
        // arbitrary JSON object with numeric fields does not, and belongs to the last-resort
        // parser, which will name its columns by position rather than by guess.
        if (frame is null || string.IsNullOrWhiteSpace(frame.Variable)) return null;

        return new TelemetryPacket
        {
            NodeId = string.IsNullOrWhiteSpace(frame.NodeId) ? raw.PortName : frame.NodeId,
            Variable = frame.Variable,
            Value = frame.Value,
            Unit = frame.Unit ?? string.Empty,

            // Still the receive time. The sender's reading is kept beside it rather than replacing
            // it: placing a remote sample on this host's timeline needs the offset between the two
            // clocks, and until that is measured and bounded, a peer whose clock is three hours out
            // would scatter its data across the chart with nothing saying why.
            Timestamp = raw.Timestamp,
            ObservedAt = ReadClock(frame.Timestamp, raw.Timestamp),

            // Carried only as far as the duplicate filter. A sender that emits neither is admitted
            // unchecked and counted as unsequenced, so nobody reads a zero duplicate count off a
            // link where nothing was ever watching.
            SourceEpoch = frame.Epoch,
            SourceSequence = frame.Sequence,

            // Provenance, not judgement, and the difference is why these two are the only flags
            // that cross. Synthetic says the value was generated rather than measured; derived says
            // it was computed from other channels. Both are facts about where the number came from
            // and stay true on any host. An anomaly score is a claim about a baseline that does not
            // travel with it.
            Flags = (frame.Simulated ? PacketFlags.Simulated : PacketFlags.None)
                    | (frame.Derived == true ? PacketFlags.IsDerived : PacketFlags.None),
            RawData = payload
        };
    }
}
