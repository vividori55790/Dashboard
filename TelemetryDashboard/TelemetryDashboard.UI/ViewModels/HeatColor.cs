namespace TelemetryDashboard.UI.ViewModels;

/// <summary>
/// An 8-bit RGB sample taken from the thermal gradient.
/// </summary>
/// <remarks>
/// A framework-free triple rather than <c>System.Windows.Media.Color</c> so gradient mapping stays
/// exercisable without a dispatcher, and so the same mapping can serve the HTML export, which needs
/// these values as CSS rather than as brushes.
/// </remarks>
/// <param name="R">Red channel, 0-255.</param>
/// <param name="G">Green channel, 0-255.</param>
/// <param name="B">Blue channel, 0-255.</param>
public sealed record HeatColor(byte R, byte G, byte B);
