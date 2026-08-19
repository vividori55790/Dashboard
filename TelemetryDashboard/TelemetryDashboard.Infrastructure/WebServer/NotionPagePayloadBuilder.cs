using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.WebServer;

/// <summary>Builds the JSON body of a Notion "create page" request from captured telemetry.</summary>
/// <remarks>
/// Split out of <see cref="NotionClient"/> because the document shape and the transport fail for
/// unrelated reasons and change on unrelated schedules. Keeping it free of I/O is also what lets
/// the same payload be written to a local backup when Notion is unreachable.
/// </remarks>
internal static class NotionPagePayloadBuilder
{
    internal static string Build(string databaseId, string title, List<TelemetryPacket>? packets)
    {
        var rows = packets ?? new List<TelemetryPacket>();

        var blocks = rows
            .Take(90) // Notion caps children per create request
            .Select(p => (object)new
            {
                @object = "block",
                type = "paragraph",
                paragraph = new
                {
                    rich_text = new[]
                    {
                        new { type = "text", text = new { content = $"{p.Timestamp:O} · {p.NodeId}.{p.Variable} = {p.Value:F4} {p.Unit}".Trim() } }
                    }
                }
            })
            .ToList();

        if (blocks.Count == 0)
        {
            blocks.Add(new
            {
                @object = "block",
                type = "paragraph",
                paragraph = new
                {
                    rich_text = new[]
                    {
                        new { type = "text", text = new { content = "No telemetry captured for this report window." } }
                    }
                }
            });
        }

        var page = new
        {
            parent = new { database_id = databaseId },
            properties = new
            {
                title = new
                {
                    title = new[] { new { text = new { content = title ?? string.Empty } } }
                }
            },
            children = blocks
        };

        return JsonSerializer.Serialize(page, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }
}
