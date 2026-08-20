using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>
/// Builds the in-process detectors an operator asked for.
/// </summary>
/// <remarks>
/// Deliberately knows nothing about external models. Everything here is arithmetic over samples the
/// host already holds, with no transport, no timeout and no failure mode beyond "not enough data
/// yet" — which is why it can live in the portable backbone. Reaching a model over a socket is a
/// different kind of thing and is assembled a layer out, where transports belong.
/// </remarks>
public static class DetectorFactory
{
    /// <summary>Builds every detector in a configuration, in file order.</summary>
    /// <exception cref="ArgumentException">A spec names settings the detector cannot accept.</exception>
    public static IReadOnlyList<IChannelDetector> Create(DetectorConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var built = new List<IChannelDetector>(configuration.Detectors.Count);
        foreach (DetectorSpec spec in configuration.Detectors)
        {
            built.Add(Create(spec));
        }
        return built;
    }

    /// <summary>Builds one detector from its description.</summary>
    /// <remarks>
    /// The <c>default</c> arm throws rather than returning a detector that judges nothing. A silent
    /// fallback here would produce a host reporting the configured number of detectors while one of
    /// them was inert, which is a worse state than refusing to start.
    /// </remarks>
    public static IChannelDetector Create(DetectorSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var channels = new ChannelSelector(spec.Channels);

        return spec.Kind?.Trim().ToLowerInvariant() switch
        {
            "mad" => new MedianAbsoluteDeviationDetector(spec.Window, spec.Threshold, channels, spec.Label),

            "ewma" => new EwmaLevelShiftDetector(spec.Window, spec.Lambda, spec.Threshold, channels, spec.Label),

            "rate" => new RateOfChangeDetector(spec.MaxRatePerSecond, spec.MaxGapSeconds, channels, spec.Label),

            "zscore" => new RollingZScoreDetector(spec.Window, spec.Threshold, spec.SampleRateHz, channels, spec.Label),

            _ => throw new ArgumentException(
                $"No detector is registered for type '{spec.Kind}'.", nameof(spec))
        };
    }

    /// <summary>Builds a panel from a configuration, ready to be asked about samples.</summary>
    public static DetectorPanel CreatePanel(DetectorConfiguration configuration) =>
        new(Create(configuration));
}
