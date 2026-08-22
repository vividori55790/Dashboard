using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// A known waveform to drive one channel with, instead of the simulator's drift.
/// </summary>
/// <remarks>
/// This exists so the analysis half of the product can be checked rather than trusted. The
/// simulator emits exactly one shape per channel — a slow sine around the setpoint plus noise, at a
/// period derived from a hash — so <see cref="Streaming.SpectrumEndpoint"/> has never had a ground
/// truth to be measured against. An operator reading a peak at 0.14 Hz has no way to know whether
/// that is the converter oscillating or the analyser being wrong; the only evidence available was
/// that the number looked plausible.
/// <para>
/// A declared signal makes the answer checkable: ask for 2 Hz, and the spectrum should report 2 Hz.
/// The same handle demonstrates a limit firing at a known amplitude and gives the DVR something
/// recognisable to have captured.
/// </para>
/// </remarks>
public sealed class InjectedSignal
{
    /// <summary><c>channel=shape@frequency:amplitude</c>, e.g. <c>dab.bus_voltage=sine@2:20</c>.</summary>
    private static readonly Regex Syntax = new(
        @"^\s*(?<channel>[A-Za-z_][A-Za-z0-9_.]*)\s*=\s*(?<shape>[A-Za-z]+)\s*@\s*(?<hz>[0-9.eE+-]+)\s*" +
        @"(?::\s*(?<amp>[0-9.eE+-]+)\s*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private InjectedSignal(string declaration, string channel, WaveformType shape, double hz, double amplitude)
    {
        Declaration = declaration;
        Channel = channel;
        Shape = shape;
        FrequencyHz = hz;
        Amplitude = amplitude;
    }

    /// <summary>The declaration as written, which is what an operator recognises in a banner.</summary>
    public string Declaration { get; }

    /// <summary>Channel this drives, spelled as the profile spells it.</summary>
    public string Channel { get; }

    public WaveformType Shape { get; }

    public double FrequencyHz { get; }

    /// <summary>Peak deviation from the channel's setpoint, in the channel's own unit.</summary>
    /// <remarks>
    /// A deviation rather than an absolute value, so a signal rides on whatever setpoint is in
    /// force and stays inside the band the profile declares. Injecting an absolute waveform would
    /// mean every declaration had to know the channel's operating point, and moving the setpoint
    /// would silently change what the signal meant.
    /// </remarks>
    public double Amplitude { get; }

    /// <summary>Parses a declaration, or explains why it is not one.</summary>
    /// <exception cref="FormatException">The declaration is malformed or names an unknown shape.</exception>
    public static InjectedSignal Parse(string declaration)
    {
        if (string.IsNullOrWhiteSpace(declaration))
        {
            throw new FormatException(
                "A signal needs a declaration, for example \"dab.bus_voltage=sine@2:20\".");
        }

        Match match = Syntax.Match(declaration);
        if (!match.Success)
        {
            throw new FormatException(
                $"'{declaration}' is not a signal. Write it as channel=shape@frequencyHz:amplitude, " +
                "for example dab.bus_voltage=sine@2:20.");
        }

        string shapeText = match.Groups["shape"].Value;
        if (!Enum.TryParse(shapeText, ignoreCase: true, out WaveformType shape)
            || !Enum.IsDefined(typeof(WaveformType), shape))
        {
            // Refused rather than defaulted to a sine. A misspelled shape that silently becomes a
            // sine produces a spectrum that looks right for the wrong reason, which is worse here
            // than anywhere else: the whole point of this feature is to be a reference.
            throw new FormatException(
                $"'{shapeText}' is not a waveform. Available: " +
                string.Join(", ", Enum.GetNames(typeof(WaveformType))).ToLowerInvariant());
        }

        double hz = Number(match.Groups["hz"].Value, declaration);
        if (hz <= 0)
        {
            throw new FormatException($"'{declaration}' asks for {hz:G6} Hz; a signal needs a positive rate.");
        }

        double amplitude = match.Groups["amp"].Success
            ? Number(match.Groups["amp"].Value, declaration)
            : 1.0;

        if (amplitude <= 0)
        {
            throw new FormatException(
                $"'{declaration}' asks for an amplitude of {amplitude:G6}; a signal with no amplitude is not one.");
        }

        return new InjectedSignal(declaration.Trim(), match.Groups["channel"].Value, shape, hz, amplitude);
    }

    /// <summary>
    /// Whether this signal would fold back into the spectrum as a false low tone.
    /// </summary>
    /// <remarks>
    /// Checked where the signal is declared rather than where it is drawn. Above Nyquist the
    /// sampled waveform is indistinguishable from a lower-frequency one, so the spectrum reports a
    /// peak that is real, wrong, and has no symptom — the reference this feature exists to provide
    /// would be quietly false.
    /// <para>
    /// <b>This checks the fundamental only, and cannot check the harmonics.</b> Every shape here
    /// except <see cref="WaveformType.Sine"/> carries them, and they fold too. Measured on a live
    /// host: a 1 Hz square sampled at 9.5 Hz put its third harmonic where theory says (3.01 Hz at
    /// 0.328 of the fundamental, against 1/3), and its fifth and seventh — at 5 Hz and 7 Hz, both
    /// past the 4.75 Hz limit — came back folded to 4.50 Hz and 2.49 Hz. Those peaks are real
    /// measurements of frequencies that are not in the signal. A square is still a useful reference
    /// for edge detection; it is not a clean one for a spectrum, and nothing here can make it so.
    /// </para>
    /// </remarks>
    public bool AliasesAt(double sampleRateHz) =>
        new SignalGeneratorService().CheckAliasingWarning(FrequencyHz, sampleRateHz);

    /// <summary>A generator armed for this signal. One per channel: each carries its own phase.</summary>
    public SignalGeneratorService Arm()
    {
        var generator = new SignalGeneratorService();
        generator.Configure(Shape, FrequencyHz, Amplitude);
        return generator;
    }

    private static double Number(string raw, string declaration) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
        && double.IsFinite(value)
            ? value
            : throw new FormatException($"'{raw}' in '{declaration}' is not a finite number.");
}
