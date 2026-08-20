using System;
using System.Buffers;
using System.Text.Json;
using TelemetryDashboard.Core.Query;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Serialises a reduced query result to the wire, metadata included.
/// </summary>
/// <remarks>
/// <para>
/// Points go out as <c>[timestampSec, value]</c> pairs rather than named objects: at two thousand
/// points a channel the field names would be most of the frame. The metadata block is not
/// optional and is never elided — a client must be able to tell a reduction from raw data, and it
/// can only do that if the answer says so every time.
/// </para>
/// <para>
/// Written through <see cref="Utf8JsonWriter"/> over a pooled buffer, so a frame costs one
/// right-sized array rather than a reflected object graph and an intermediate string.
/// </para>
/// </remarks>
public static class SeriesFrameWriter
{
    /// <summary>Rough bytes per emitted point, used to size the buffer on the first attempt.</summary>
    private const int BytesPerPointEstimate = 40;

    public static ReadOnlyMemory<byte> Write(SeriesQueryResult result, double serverTimeSec)
    {
        ArgumentNullException.ThrowIfNull(result);

        var buffer = new ArrayBufferWriter<byte>(
            512 + (int)Math.Min(result.ReturnedPointCount * BytesPerPointEstimate, 32_000_000));

        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteFrame(writer, result, serverTimeSec);
        }

        return buffer.WrittenMemory;
    }

    private static void WriteFrame(Utf8JsonWriter writer, SeriesQueryResult result, double serverTimeSec)
    {
        SeriesQueryRequest request = result.Request;

        writer.WriteStartObject();
        writer.WriteString("type", "series");
        writer.WriteNumber("serverTimeSec", serverTimeSec);

        // The one field a naive client is most likely to read. It is true whenever any channel in
        // this frame went through a lossy reduction, so "false" is a positive statement that these
        // points are the samples themselves.
        writer.WriteBoolean("isReduced", result.IsReduced);
        writer.WriteNumber("sourceSampleCount", result.SourceSampleCount);
        writer.WriteNumber("returnedPointCount", result.ReturnedPointCount);
        writer.WriteNumber("compressionRatio", Round(result.CompressionRatio));

        writer.WriteStartObject("request");
        writer.WriteNumber("startSec", request.StartSec);
        writer.WriteNumber("endSec", request.EndSec);
        writer.WriteNumber("maxPoints", request.MaxPoints);
        writer.WriteString("reduction", SubscriptionRequestParser.NameOf(request.Method));
        writer.WriteEndObject();

        writer.WriteStartArray("series");
        foreach (ReducedSeries series in result.Series) WriteSeries(writer, series);
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    private static void WriteSeries(Utf8JsonWriter writer, ReducedSeries series)
    {
        writer.WriteStartObject();
        writer.WriteString("channel", series.Channel);

        writer.WriteStartArray("points");
        foreach (SeriesPoint point in series.Points)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(point.TimestampSec);
            writer.WriteNumberValue(point.Value);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();

        WriteMetadata(writer, series.Metadata);
        writer.WriteEndObject();
    }

    private static void WriteMetadata(Utf8JsonWriter writer, ReductionMetadata metadata)
    {
        writer.WriteStartObject("reduction");
        writer.WriteString("method", SubscriptionRequestParser.NameOf(metadata.Method));
        writer.WriteBoolean("preservesExtremes", metadata.PreservesExtremes);
        writer.WriteNumber("sourceSampleCount", metadata.SourceSampleCount);
        writer.WriteNumber("returnedPointCount", metadata.ReturnedPointCount);
        writer.WriteNumber("discardedSampleCount", metadata.DiscardedSampleCount);
        writer.WriteNumber("compressionRatio", Round(metadata.CompressionRatio));
        writer.WriteNumber("bucketWidthSec", metadata.BucketWidthSec);
        writer.WriteNumber("windowStartSec", metadata.WindowStartSec);
        writer.WriteNumber("windowEndSec", metadata.WindowEndSec);
        writer.WriteBoolean("windowTruncatedByRetention", metadata.WindowTruncatedByRetention);
        WriteFiniteNumber(writer, "sourceMinimum", metadata.SourceMinimum);
        WriteFiniteNumber(writer, "sourceMaximum", metadata.SourceMaximum);
        writer.WriteString("discarded", metadata.DiscardedDescription);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes a measured extreme, or JSON <c>null</c> when there was nothing to measure.
    /// </summary>
    /// <remarks>
    /// An empty channel has no minimum. Emitting 0 for it would put a number on a chart's axis
    /// that no sensor produced, which is the exact failure this API is built to avoid.
    /// </remarks>
    private static void WriteFiniteNumber(Utf8JsonWriter writer, string name, double value)
    {
        if (double.IsFinite(value)) writer.WriteNumber(name, value);
        else writer.WriteNull(name);
    }

    private static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
}
