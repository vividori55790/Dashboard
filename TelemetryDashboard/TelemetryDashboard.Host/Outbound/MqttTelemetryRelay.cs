using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Infrastructure.WebServer;

namespace TelemetryDashboard.Host.Outbound;

/// <summary>
/// Republishes every scored sample to an MQTT broker, one topic per channel.
/// </summary>
/// <remarks>
/// This is how the hub joins a plant that already has a broker: historians, SCADA screens and
/// other subscribers get the same numbers the browser console shows, with the same provenance
/// attached. The publisher underneath speaks MQTT 3.1.1 on the wire and had no caller anywhere,
/// so the integration existed as code and not as a capability.
///
/// The z-score is omitted from the payload when the host reached no verdict, rather than sent as
/// zero. A subscriber that sees the field is looking at a judgement; one that does not is looking
/// at a sample the host had not yet learned enough to judge.
/// </remarks>
public sealed class MqttTelemetryRelay : IAsyncDisposable
{
    private readonly MqttPublisher _publisher;
    private readonly string _topicPrefix;
    private readonly OutboundQueue<(string Topic, string Payload)> _queue;

    public MqttTelemetryRelay(MqttPublisher publisher, string topicPrefix)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _topicPrefix = (topicPrefix ?? "telemetry").TrimEnd('/');
        _queue = new OutboundQueue<(string, string)>(
            "mqtt", capacity: 10_000, (item, _) => _publisher.PublishAsync(item.Item1, item.Item2));
    }

    /// <summary>Whether the broker handshake has completed.</summary>
    public bool IsConnected => _publisher.IsConnected;

    /// <summary>Opens the broker connection. Returns false rather than throwing.</summary>
    public async Task<bool> ConnectAsync(string host, int port)
    {
        try
        {
            return await _publisher.ConnectAsync(host, port).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Handler for the publisher's scored-sample event.</summary>
    public void OnSampleScored(object? sender, ScoredSample sample) =>
        _queue.Offer((TopicFor(sample), Payload(sample)));

    /// <summary>Topic for one channel. Public so the layout can be asserted.</summary>
    public string TopicFor(ScoredSample sample) =>
        $"{_topicPrefix}/{Segment(sample.NodeId)}/{Segment(sample.Variable)}";

    /// <summary>
    /// Builds the JSON payload. Public so a subscriber contract can be asserted without a broker.
    /// </summary>
    /// <remarks>
    /// Nulls are omitted rather than emitted. A subscriber that receives no <c>zscore</c> knows the
    /// host reached no verdict; one that received <c>0</c> would read it as a calm channel.
    /// </remarks>
    public static string Payload(ScoredSample sample) => JsonSerializer.Serialize(
        new
        {
            node = sample.NodeId,
            variable = sample.Variable,
            value = sample.Value,
            unit = sample.Unit,
            timestamp = sample.TimestampUtc.ToString("o"),
            simulated = sample.IsSimulated,
            zscore = sample.ZScore,
            isAnomaly = sample.IsAnomaly,
            analyzerId = sample.AnalyzerId,

            // Absent unless the reading is outside a band, so a subscriber sees the field only
            // when it means something -- and separate from isAnomaly, which answers a different
            // question and cannot answer this one: a channel sitting steadily outside its safe
            // band is not unusual to a rolling detector.
            outsideLimit = sample.BreachesALimit ? true : (bool?)null,
            limits = sample.BreachedLimits?
                .Where(l => l.IsOutside)
                .Select(l => l.Rule.Declaration)
                .ToArray() is { Length: > 0 } declarations ? declarations : null
        },
        PayloadOptions);

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>MQTT topic levels are separated by '/', so a name containing one would split it.</summary>
    private static string Segment(string value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0) return "unnamed";

        return trimmed.Replace('/', '_').Replace('+', '_').Replace('#', '_');
    }

    /// <summary>One line for the shutdown report, or null when nothing was ever published.</summary>
    public string? Summary() => _queue.Summary();

    public async ValueTask DisposeAsync()
    {
        await _queue.DisposeAsync().ConfigureAwait(false);
        _publisher.Dispose();
    }
}
