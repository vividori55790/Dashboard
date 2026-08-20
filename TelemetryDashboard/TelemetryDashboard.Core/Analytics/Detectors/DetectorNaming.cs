using System.Globalization;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>
/// Builds the identity string a detector stamps onto every verdict it issues.
/// </summary>
/// <remarks>
/// Internal on purpose: the format is a convention shared by the detectors in this folder, not a
/// contract anything outside should construct or parse. What matters outside is that the string is
/// stable for a given configuration and different for a different one, so two detectors of the same
/// kind running over one channel can be told apart in a stored record.
/// </remarks>
internal static class DetectorNaming
{
    /// <summary>
    /// <c>kind/settings</c>, prefixed with the operator's own label when they gave one.
    /// </summary>
    /// <remarks>
    /// The settings are never omitted, even when a label is present. A label says which entry in the
    /// configuration file this was; the settings say what it actually computed, and those diverge
    /// the moment someone edits the file without renaming the entry.
    /// </remarks>
    public static string Compose(string? label, string kind, string settings)
    {
        string core = string.IsNullOrWhiteSpace(settings) ? kind : kind + "/" + settings;
        return string.IsNullOrWhiteSpace(label) ? core : label!.Trim() + ":" + core;
    }

    /// <summary>A number formatted the same way on every machine, so an id does not vary by locale.</summary>
    public static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
