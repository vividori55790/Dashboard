namespace TelemetryDashboard.Core.Analytics;

/// <summary>A fitted linear trend and the evidence for it.</summary>
/// <param name="SlopePerSample">Least-squares slope, in channel units per sample.</param>
/// <param name="RSquared">Share of the variation the line explains, 0 to 1.</param>
/// <param name="Samples">How many samples the fit was computed from.</param>
public readonly record struct TrendFit(double SlopePerSample, double RSquared, int Samples)
{
    /// <summary>
    /// How much of the variation the line must explain before a forecast is worth stating.
    /// </summary>
    /// <remarks>
    /// A judgement call, and worth naming as one. Half the variance is the point where the line is
    /// describing the data rather than passing through it; below that the slope is mostly an
    /// artefact of where the window happens to start. There is no threshold that is correct for
    /// every signal, which is exactly why it is a named constant rather than a number buried in a
    /// condition.
    /// </remarks>
    public const double MinimumRSquared = 0.5;

    /// <summary>Fewest samples that can support a forecast at all.</summary>
    public const int MinimumSamples = 5;

    /// <summary>
    /// Whether extrapolating this trend states something the data supports.
    /// </summary>
    /// <remarks>
    /// When this is false the honest answer is no forecast, not a forecast with a caveat. A number
    /// on a dashboard is read as a number; a caveat beside it is read as decoration.
    /// </remarks>
    public bool SupportsForecast => Samples >= MinimumSamples && RSquared >= MinimumRSquared;
}
