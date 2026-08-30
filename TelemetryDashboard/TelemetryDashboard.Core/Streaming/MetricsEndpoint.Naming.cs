using System;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// What a series key means: which parts of it are identity, and which keys are not readings.
/// </summary>
/// <remarks>
/// Both questions are about interpreting the same string, and both are answered here rather
/// than where the families are written -- a caller writing a gauge should not also be deciding
/// whether the thing it is writing is a measurement.
/// </summary>
public static partial class MetricsEndpoint
{
    /// <summary>
    /// Series that are this host's judgement about a channel rather than a reading of one.
    /// </summary>
    /// <remarks>
    /// <c>TelemetryFrameRecorder</c> records every numeric field of a frame as its own series, so a
    /// scored channel acquires <c>.predicted</c> and <c>.predictedHorizonSec</c> beside it. Inside
    /// this product that is right and the console labels them; on this endpoint it is not, because
    /// nothing downstream can tell them apart. A live scrape carried
    /// <c>telemetry_channel_value{channel="SIM:generic-machine.ambient.temperature.predicted"}</c>
    /// with no mark of any kind -- a forecast, presented to somebody else's alerting rules as a
    /// measurement.
    /// <para>
    /// ARCHITECTURE's worked example is about this exact number: the forecast was withheld for 92%
    /// of channels because a fitted line that explains nothing is not a prediction. Publishing the
    /// surviving 8% as though an instrument reported it undoes that at the boundary, which is the
    /// one place the withholding cannot be seen.
    /// </para>
    /// <para>
    /// Excluded rather than renamed. A <c>telemetry_channel_forecast</c> family would be more
    /// informative and is worth having; it also needs the horizon beside it to mean anything --
    /// <c>predictedHorizonSec</c> is not a reading either -- and that is a second metric with its
    /// own naming to get right. Recorded as owed rather than half-built.
    /// </para>
    /// </remarks>
    private static readonly string[] VerdictSuffixes =
        [".predicted", ".predictedHorizonSec", ".anomalyScore", ".zScore", ".drift"];

    private static bool IsThisHostsOwnVerdict(string channel)
    {
        foreach (string suffix in VerdictSuffixes)
        {
            if (channel.EndsWith(suffix, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>The observing node, and the channel within it.</summary>
    /// <remarks>
    /// Two labels, not one. The series store keys a channel as <c>node.variable</c>, and exporting
    /// that whole string as a single <c>channel</c> label reads as faithful and is not: Prometheus's
    /// own conventions say to use labels for the dimensions rather than encoding them in a name, and
    /// ARCHITECTURE §2 says the same thing in its own words -- identity is which host observed it,
    /// which device it came from and which quantity it is, three parts and not one. Glued together,
    /// nobody downstream can group by node without a regex over a label value.
    /// <para>
    /// This was found by generating a Grafana dashboard against it. Every panel imported cleanly and
    /// matched nothing, because the generator asked for <c>{node=..., channel=...}</c> -- which is
    /// the right question. The split is on the first dot, because that is where
    /// <c>IngestPublisher</c> joins them.
    /// </para>
    /// <para>
    /// A series with no dot at all gets no node label rather than an invented one. It means the
    /// publisher did not name a node, and an empty string presented as a node id is a claim.
    /// </para>
    /// </remarks>
    private static string Node(string channel)
    {
        int dot = channel.IndexOf('.');
        return dot > 0 ? channel[..dot] : string.Empty;
    }

    private static string Within(string channel)
    {
        int dot = channel.IndexOf('.');
        return dot > 0 ? channel[(dot + 1)..] : channel;
    }

    /// <summary>Writes one channel sample with the labels its key actually supports.</summary>
    /// <remarks>
    /// A key with no dot gets no <c>node</c> label rather than an empty one. Prometheus matches an
    /// empty label value and an absent label alike, so this changes no query -- it changes what the
    /// document says. <c>node=""</c> reads as a node whose name is the empty string, and this
    /// codebase spent the afternoon removing exactly that kind of claim from the other endpoints.
    /// </remarks>
    private static void SampleChannel(Family family, double value, string channel)
    {
        string node = Node(channel);

        if (node.Length == 0) family.Sample(value, "channel", Within(channel));
        else family.Sample(value, "node", node, "channel", Within(channel));
    }
}
