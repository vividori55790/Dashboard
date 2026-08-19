namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// Waveform shapes the interactive signal generator can synthesise.
/// </summary>
/// <remarks>
/// Declared next to the generator rather than in <c>Core.Models</c> because it is part of the
/// generator's call contract: adding or reordering a member changes the meaning of every stored
/// generator profile, so the two must be reviewed together.
/// <para>
/// The file lives under <c>Analytics/</c> with the rest of the DSP code but publishes into the
/// <c>Core.Services</c> namespace alongside the other service-layer contracts callers consume.
/// </para>
/// </remarks>
public enum WaveformType
{
    /// <summary>Pure tone. The fallback for any unrecognised configuration.</summary>
    Sine,

    /// <summary>Hard-switching two-level output, useful for exercising edge detection.</summary>
    Square,

    /// <summary>Linear rise and fall — constant slew rate in both directions.</summary>
    Triangle,

    /// <summary>Linear ramp with an instantaneous reset, for testing discontinuity handling.</summary>
    Sawtooth,

    /// <summary>Uniform white noise, used to calibrate anomaly thresholds against a known floor.</summary>
    Noise
}
