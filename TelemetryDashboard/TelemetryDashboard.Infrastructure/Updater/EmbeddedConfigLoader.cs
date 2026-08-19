using System.Text.Json;

namespace TelemetryDashboard.Infrastructure.Updater;

/// <summary>Reads the configuration blob embedded in the single-file portable build.</summary>
/// <remarks>
/// Paired with <see cref="EmbeddedConfig"/> rather than left on the package checker, because the
/// blob's format and its defaults are one decision. Every failure path returns a populated config
/// carrying a <see cref="EmbeddedConfig.FallbackReason"/> instead of throwing: a portable build
/// whose blob is corrupt must still start, and the reason has to survive far enough to be shown.
/// </remarks>
internal static class EmbeddedConfigLoader
{
    /// <summary>Parses the blob, substituting documented defaults when it cannot be read.</summary>
    internal static EmbeddedConfig Load(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return new EmbeddedConfig { UseDefaults = true, FallbackReason = "Embedded configuration was empty." };
        }

        try
        {
            EmbeddedConfig? parsed = JsonSerializer.Deserialize<EmbeddedConfig>(configJson);
            if (parsed is null)
            {
                return new EmbeddedConfig { UseDefaults = true, FallbackReason = "Embedded configuration deserialized to null." };
            }

            return parsed;
        }
        catch (JsonException ex)
        {
            return new EmbeddedConfig { UseDefaults = true, FallbackReason = $"Embedded configuration is not valid JSON: {ex.Message}" };
        }
    }
}
