using System;
using System.Collections.Generic;
using System.Text.Json;
using TelemetryDashboard.Core.Query;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>What an upstream message from a client turned out to be.</summary>
public enum SubscriptionCommandKind
{
    /// <summary>Not a subscription message; it belongs to the application command path.</summary>
    NotACommand = 0,
    Subscribe = 1,
    Unsubscribe = 2
}

/// <summary>
/// Reads the client's standing request off the WebSocket.
/// </summary>
/// <remarks>
/// Wire form, all fields optional except <c>channels</c>:
/// <code>
/// {"type":"subscribe","channels":["NODE.temp"],"maxUpdateHz":10,
///  "maxPoints":2000,"windowSec":60,"reduction":"minmax"}
/// {"type":"unsubscribe"}
/// </code>
/// A message this parser does not recognise is left alone and reaches the application's command
/// handler unchanged, so adding subscriptions did not take the command channel away from anyone.
/// </remarks>
public static class SubscriptionRequestParser
{
    public static SubscriptionCommandKind Parse(string? json, out SubscriptionOptions? options)
    {
        options = null;
        if (string.IsNullOrWhiteSpace(json)) return SubscriptionCommandKind.NotACommand;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return SubscriptionCommandKind.NotACommand;

            if (!root.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String)
            {
                return SubscriptionCommandKind.NotACommand;
            }

            string? kind = type.GetString();
            if (string.Equals(kind, "unsubscribe", StringComparison.OrdinalIgnoreCase))
            {
                return SubscriptionCommandKind.Unsubscribe;
            }

            if (!string.Equals(kind, "subscribe", StringComparison.OrdinalIgnoreCase))
            {
                return SubscriptionCommandKind.NotACommand;
            }

            options = new SubscriptionOptions(
                ReadChannels(root),
                ReadDouble(root, "maxUpdateHz", SubscriptionOptions.DefaultMaxUpdateHz),
                (int)ReadDouble(root, "maxPoints", SubscriptionOptions.DefaultMaxPoints),
                ReadDouble(root, "windowSec", SubscriptionOptions.DefaultWindowSec),
                ReadMethod(root));

            return SubscriptionCommandKind.Subscribe;
        }
        catch (JsonException)
        {
            return SubscriptionCommandKind.NotACommand;
        }
    }

    /// <summary>Canonical wire name for a reduction, so a client can echo what it was given.</summary>
    public static string NameOf(ReductionMethod method) => method switch
    {
        ReductionMethod.MinMax => "minmax",
        ReductionMethod.LargestTriangleThreeBuckets => "lttb",
        _ => "none"
    };

    private static IReadOnlyList<string> ReadChannels(JsonElement root)
    {
        if (!root.TryGetProperty("channels", out JsonElement channels) ||
            channels.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>(channels.GetArrayLength());
        foreach (JsonElement entry in channels.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String) continue;
            string? name = entry.GetString();
            if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
        }
        return names;
    }

    private static double ReadDouble(JsonElement root, string field, double fallback) =>
        root.TryGetProperty(field, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out double parsed)
            ? parsed
            : fallback;

    private static ReductionMethod ReadMethod(JsonElement root)
    {
        if (!root.TryGetProperty("reduction", out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return ReductionMethod.MinMax;
        }

        return value.GetString()?.ToLowerInvariant() switch
        {
            "lttb" or "largest-triangle-three-buckets" => ReductionMethod.LargestTriangleThreeBuckets,
            "none" or "raw" => ReductionMethod.None,
            _ => ReductionMethod.MinMax
        };
    }
}
