using System;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// Software function generator that drives the dashboard with known-good signals when no hardware
/// is attached.
/// </summary>
/// <remarks>
/// Phase is tracked as a fraction of one cycle in [0,1) rather than as accumulated radians. A
/// radian accumulator grows without bound: after a few minutes at kilohertz rates it is large
/// enough that adding one small increment loses the low bits, and the waveform visibly quantises.
/// Wrapping a fraction every cycle keeps the operand small for as long as the app runs.
/// <para>
/// The file lives under <c>Analytics/</c> with the rest of the DSP code but publishes into the
/// <c>Core.Services</c> namespace alongside the other service-layer contracts.
/// </para>
/// </remarks>
public sealed class SignalGeneratorService
{
    private readonly Random _noiseSource = new();

    /// <summary>Position within the current cycle, in [0,1).</summary>
    private double _phase;

    /// <summary>Peak amplitude of the synthesised waveform. Never negative.</summary>
    public double Amplitude { get; private set; }

    /// <summary>Configured frequency in hertz. Never negative.</summary>
    public double FrequencyHz { get; private set; }

    /// <summary>Waveform currently being synthesised.</summary>
    public WaveformType CurrentWaveform { get; private set; } = WaveformType.Sine;

    /// <summary>True between <see cref="Configure"/> and <see cref="Stop"/>.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Arms the generator with a waveform, frequency and amplitude, restarting its phase.
    /// </summary>
    /// <remarks>
    /// Inputs are coerced rather than rejected. This is driven from sliders and typed fields where
    /// a value is transiently negative, empty or NaN mid-edit; throwing there would tear down the
    /// render loop over a half-finished keystroke. A negative amplitude describes the same waveform
    /// inverted, so its magnitude is the honest reading, and an unrecognised enum value — an old
    /// profile, a hand-edited layout file — falls back to the one waveform every caller understands.
    /// </remarks>
    public void Configure(WaveformType type, double frequencyHz, double amplitude)
    {
        CurrentWaveform = Enum.IsDefined(typeof(WaveformType), type) ? type : WaveformType.Sine;
        FrequencyHz = Magnitude(frequencyHz);
        Amplitude = Magnitude(amplitude);
        _phase = 0.0;
        IsRunning = true;
    }

    /// <summary>
    /// Advances the generator by <paramref name="deltaSeconds"/> and returns the next sample.
    /// </summary>
    /// <remarks>
    /// A zero frequency short-circuits to zero, which is also what the maths gives: a stopped
    /// waveform sits at its own phase-zero crossing, and every shape here crosses zero there.
    /// </remarks>
    public double GetNextSample(double deltaSeconds)
    {
        if (!IsRunning || FrequencyHz <= 0.0 || !double.IsFinite(deltaSeconds)) return 0.0;

        _phase = (_phase + FrequencyHz * deltaSeconds) % 1.0;
        if (_phase < 0.0) _phase += 1.0;   // a rewound clock must not read as a negative phase

        return Amplitude * UnitWaveform(_phase);
    }

    /// <summary>
    /// True when <paramref name="freqHz"/> sits above the Nyquist limit of
    /// <paramref name="sampleRateHz"/> and would fold back into the spectrum as a false low tone.
    /// </summary>
    /// <remarks>
    /// A non-positive or non-finite sample rate warns unconditionally: there is no Nyquist ceiling
    /// to compare against, so no frequency can be reproduced faithfully.
    /// </remarks>
    public bool CheckAliasingWarning(double freqHz, double sampleRateHz)
    {
        if (!double.IsFinite(freqHz) || !double.IsFinite(sampleRateHz) || sampleRateHz <= 0.0) return true;

        return Math.Abs(freqHz) > sampleRateHz / 2.0;
    }

    /// <summary>
    /// Disarms the generator and rewinds its phase. Safe to call when already stopped.
    /// </summary>
    /// <remarks>
    /// Idempotent because it is wired to both a toolbar button and the window-closing path, and
    /// making the second call an error would only produce a shutdown crash nobody can act on.
    /// </remarks>
    public void Stop()
    {
        IsRunning = false;
        _phase = 0.0;
    }

    /// <summary>Unit-amplitude sample of the configured shape at a phase in [0,1).</summary>
    private double UnitWaveform(double phase) => CurrentWaveform switch
    {
        WaveformType.Square => phase < 0.5 ? 1.0 : -1.0,
        WaveformType.Triangle => 2.0 * Math.Abs(2.0 * phase - 1.0) - 1.0,
        WaveformType.Sawtooth => 2.0 * phase - 1.0,
        WaveformType.Noise => _noiseSource.NextDouble() * 2.0 - 1.0,
        _ => Math.Sin(2.0 * Math.PI * phase)
    };

    /// <summary>Absolute value of a user-supplied setting, with non-finite input read as zero.</summary>
    private static double Magnitude(double value) => double.IsFinite(value) ? Math.Abs(value) : 0.0;
}
