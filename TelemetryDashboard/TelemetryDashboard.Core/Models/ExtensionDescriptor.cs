using System;

namespace TelemetryDashboard.Core.Models;

/// <summary>
/// Metadata describing a distributable extension in the marketplace.
/// </summary>
public sealed class ExtensionDescriptor
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Lowest host API version this extension can run against.</summary>
    public string MinApiVersion { get; set; } = "1.0.0";

    /// <summary>
    /// True when a host advertising <paramref name="hostApiVersion"/> satisfies
    /// <see cref="MinApiVersion"/>. An unparseable version on either side is treated as
    /// incompatible: loading an extension whose requirements cannot be read is a gamble
    /// with the host process.
    /// </summary>
    public bool IsCompatibleWithApiVersion(string hostApiVersion)
    {
        // Fully qualified: the Version property above shadows the System.Version type here.
        if (!System.Version.TryParse(Normalize(MinApiVersion), out System.Version? required)) return false;
        if (!System.Version.TryParse(Normalize(hostApiVersion), out System.Version? host)) return false;

        return host >= required;
    }

    /// <summary>Strips a leading 'v' so "v1.2.0" and "1.2.0" compare equal.</summary>
    private static string Normalize(string? version)
    {
        string text = (version ?? string.Empty).Trim();
        return text.StartsWith('v') || text.StartsWith('V') ? text[1..] : text;
    }
}
