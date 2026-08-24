using System;
using System.Globalization;

namespace TelemetryDashboard.Host.Configuration;

public sealed partial class SniffCommandLine
{
    /// <summary>
    /// How long to listen, written the way somebody says it out loud.
    /// </summary>
    /// <remarks>
    /// Split out when --verify pushed the parser past the 150-line rule, and split here rather
    /// than exempted because this is the one part of the file with a job of its own: it turns a
    /// word into a TimeSpan and knows nothing about sniffing. The rest is the command's shape.
    /// <para>
    /// InvariantCulture, deliberately. A duration typed as 1.5 must mean the same thing on a
    /// machine whose locale uses a decimal comma, and PortabilityHazardTests fails a
    /// double.Parse on this path that does not say so.
    /// </para>
    /// </remarks>
    /// <summary>Reads <c>15s</c>, <c>2m</c> or a bare number of seconds.</summary>
    public static bool TryDuration(string? text, out TimeSpan duration)
    {
        duration = DefaultDuration;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string trimmed = text.Trim();
        double scale = 1.0;

        if (trimmed.EndsWith('s')) trimmed = trimmed[..^1];
        else if (trimmed.EndsWith('m')) { trimmed = trimmed[..^1]; scale = 60.0; }

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return false;
        if (value <= 0) return false;

        duration = TimeSpan.FromSeconds(value * scale);
        return true;
    }
}
