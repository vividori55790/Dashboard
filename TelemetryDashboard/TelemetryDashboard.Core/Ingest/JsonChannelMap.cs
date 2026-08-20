using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Ingest;

/// <summary>One channel extracted from a JSON document, named by a dotted path.</summary>
/// <param name="Variable">Channel name as it will appear on charts.</param>
/// <param name="Path">Dotted path into the document, e.g. <c>length.new</c> or <c>data.k.c</c>.</param>
/// <param name="Unit">Physical unit, empty when dimensionless.</param>
public sealed record JsonChannel(string Variable, string Path, string Unit = "");

/// <summary>
/// Projects an arbitrary JSON document onto telemetry channels, by configuration rather than by code.
/// </summary>
/// <remarks>
/// Written this way on purpose. A source adapter hardcoded to one provider is how this project
/// ended up welded to a single power-converter example, and the same mistake repeats the moment
/// somebody writes a class called <c>BinanceSource</c>. A map is data: pointing the hub at a
/// different feed is a file, not a rebuild.
///
/// The honesty rule governs every extraction here. A path that is absent, null, or not a number
/// yields <em>no packet at all</em> — never zero. A feed that stops sending a field must produce a
/// gap in the chart, because a gap is what happened; substituting zero would draw a cliff to the
/// floor and every downstream mean would follow it.
/// </remarks>
public sealed class JsonChannelMap
{
    public JsonChannelMap(
        string name,
        IReadOnlyList<JsonChannel> channels,
        string? nodePath = null,
        string nodeFallback = "json")
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A map must be named.", nameof(name));
        if (channels is null || channels.Count == 0)
        {
            throw new ArgumentException("A map with no channels would silently ingest nothing.", nameof(channels));
        }

        Name = name;
        Channels = channels;
        NodePath = nodePath;
        NodeFallback = string.IsNullOrWhiteSpace(nodeFallback) ? "json" : nodeFallback;
    }

    public string Name { get; }

    public IReadOnlyList<JsonChannel> Channels { get; }

    /// <summary>Path whose value names the reporting node, or null to use <see cref="NodeFallback"/>.</summary>
    public string? NodePath { get; }

    /// <summary>Node name used when <see cref="NodePath"/> is absent or empty.</summary>
    public string NodeFallback { get; }

    /// <summary>Documents seen that produced no channel at all.</summary>
    /// <remarks>
    /// Counted rather than ignored. A map whose paths do not match the feed produces silence, and
    /// silence is indistinguishable from a feed that has stopped. This number is how an operator
    /// tells "my mapping is wrong" from "the exchange is down".
    /// </remarks>
    public long UnmatchedDocuments { get; private set; }

    /// <summary>Documents that were not valid JSON.</summary>
    public long MalformedDocuments { get; private set; }

    /// <summary>Documents that produced at least one channel.</summary>
    public long MatchedDocuments { get; private set; }

    /// <summary>Projects one document. Returns an empty list rather than throwing.</summary>
    public IReadOnlyList<TelemetryPacket> Project(string json, DateTime observedUtc, string portName = "")
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<TelemetryPacket>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            MalformedDocuments++;
            return Array.Empty<TelemetryPacket>();
        }

        using (document)
        {
            string node = ResolveNode(document.RootElement);
            var packets = new List<TelemetryPacket>(Channels.Count);

            foreach (JsonChannel channel in Channels)
            {
                if (!TryReadNumber(document.RootElement, channel.Path, out double value)) continue;

                packets.Add(new TelemetryPacket
                {
                    NodeId = node,
                    Variable = channel.Variable,
                    Value = value,
                    Unit = channel.Unit,
                    Timestamp = observedUtc,
                    RawData = json.Length <= 512 ? json : json[..512]
                });
            }

            if (packets.Count == 0) UnmatchedDocuments++;
            else MatchedDocuments++;

            return packets;
        }
    }

    private string ResolveNode(JsonElement root)
    {
        if (NodePath is null) return NodeFallback;
        if (!TryResolve(root, NodePath, out JsonElement element)) return NodeFallback;

        string? text = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };

        return string.IsNullOrWhiteSpace(text) ? NodeFallback : text!;
    }

    /// <summary>
    /// Reads a number at a path, accepting a JSON number or a string that holds one.
    /// </summary>
    /// <remarks>
    /// Several public feeds send prices and volumes as quoted strings to avoid float rounding in
    /// transit. Refusing those would mean the map silently matched nothing on a working feed.
    /// Non-finite values are refused: an infinity is not a reading, and admitting one would poison
    /// every rolling mean it entered.
    /// </remarks>
    private static bool TryReadNumber(JsonElement root, string path, out double value)
    {
        value = 0;
        if (!TryResolve(root, path, out JsonElement element)) return false;

        bool parsed = element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(
                element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            _ => false
        };

        return parsed && double.IsFinite(value);
    }

    private static bool TryResolve(JsonElement root, string path, out JsonElement found)
    {
        found = root;

        foreach (string segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (found.ValueKind == JsonValueKind.Array && int.TryParse(segment, out int index))
            {
                if (index < 0 || index >= found.GetArrayLength()) return false;
                found = found[index];
                continue;
            }

            if (found.ValueKind != JsonValueKind.Object || !found.TryGetProperty(segment, out found)) return false;
        }

        return true;
    }
}
