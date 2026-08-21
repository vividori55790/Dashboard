using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Query;

namespace TelemetryDashboard.Core.Streaming;

/// <summary>
/// Answers <c>/api/spectrum</c>: the magnitude spectrum of one channel's recent window.
/// </summary>
/// <remarks>
/// A rotating machine, a switching converter and a mains feed all state their faults in the
/// frequency domain before they state them in the time domain — a bearing starts a sideband long
/// before its temperature moves. <see cref="FftAnalyzerService"/> has been able to compute this
/// since M2 and lived in the WPF view-model folder, where the headless host is forbidden to reach
/// it, so no browser could ever ask for a spectrum.
/// <para>
/// The sample rate is measured from the timestamps rather than configured. A telemetry stream does
/// not arrive on a metronome — a poller, a serial device and a simulator all jitter — and a
/// spectrum labelled with an assumed rate puts every peak at the wrong frequency while looking
/// entirely plausible.
/// </para>
/// </remarks>
public static class SpectrumEndpoint
{
    /// <summary>Fewest samples worth transforming.</summary>
    /// <remarks>
    /// Below this the bins are wider than anything they could resolve, and returning a spectrum
    /// anyway would invite a reader to point at a peak that is an artefact of the window length.
    /// </remarks>
    public const int MinimumSamples = 16;

    /// <summary>Default span of history to transform.</summary>
    public const double DefaultWindowSec = 60.0;

    /// <summary>The answer, shaped for JSON. Every field is measured, none assumed.</summary>
    public sealed record Result
    {
        public string Status { get; init; } = "Success";
        public string Channel { get; init; } = string.Empty;

        /// <summary>Why there is no spectrum, when there is none.</summary>
        public string? Reason { get; init; }

        public int Samples { get; init; }
        public double WindowSec { get; init; }

        /// <summary>Samples per second, derived from the timestamps actually received.</summary>
        public double SampleRateHz { get; init; }

        /// <summary>Highest frequency this sampling can represent.</summary>
        public double NyquistHz { get; init; }

        /// <summary>Width of one bin, in hertz.</summary>
        public double BinHz { get; init; }

        /// <summary>Bin frequencies, ascending.</summary>
        public IReadOnlyList<double> Frequencies { get; init; } = Array.Empty<double>();

        /// <summary>Magnitude per bin, aligned with <see cref="Frequencies"/>.</summary>
        public IReadOnlyList<double> Magnitudes { get; init; } = Array.Empty<double>();

        /// <summary>Frequency of the largest bin above DC, or null when there is no such peak.</summary>
        /// <remarks>
        /// DC is excluded because it is the mean, which is the largest bin for almost every
        /// telemetry channel and says nothing about periodicity. Reporting it as "the peak" would
        /// make every spectrum look like it had found something.
        /// </remarks>
        public double? PeakHz { get; init; }

        public double? PeakMagnitude { get; init; }
    }

    /// <summary>Computes the spectrum of <paramref name="channel"/> over the last window.</summary>
    public static Result Compute(SeriesStore store, string channel, double windowSec, double nowSec)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (string.IsNullOrWhiteSpace(channel))
        {
            return new Result { Status = "Error", Reason = "no channel named; pass ?channel=<id>" };
        }

        ChannelSeriesBuffer? buffer = store.Find(channel);
        if (buffer is null)
        {
            return new Result
            {
                Status = "Error",
                Channel = channel,
                Reason = $"no channel '{channel}' has been received"
            };
        }

        double span = windowSec > 0 ? windowSec : DefaultWindowSec;
        var points = new SeriesPoint[buffer.Count];
        int taken = buffer.CopyWindow(nowSec - span, nowSec, points);

        if (taken < MinimumSamples)
        {
            return new Result
            {
                Status = "Error",
                Channel = channel,
                Samples = taken,
                WindowSec = span,
                Reason = $"only {taken} sample(s) in the last {span:0.#}s; {MinimumSamples} are needed"
            };
        }

        // Measured, not assumed: the elapsed span divided by the intervals it contains.
        double elapsed = points[taken - 1].TimestampSec - points[0].TimestampSec;
        if (elapsed <= 0)
        {
            return new Result
            {
                Status = "Error",
                Channel = channel,
                Samples = taken,
                WindowSec = span,
                Reason = "every sample carries the same timestamp, so no rate can be derived"
            };
        }

        double rate = (taken - 1) / elapsed;

        double[] values = new double[taken];
        for (int i = 0; i < taken; i++) values[i] = points[i].Value;

        // The mean is removed first. Leaving it in puts the whole signal energy in bin 0 and
        // scales every other bin down beside it, which hides exactly the small periodic component
        // this endpoint exists to surface.
        double mean = values.Average();
        for (int i = 0; i < taken; i++) values[i] -= mean;

        double[] magnitudes = new FftAnalyzerService().ComputeFft(values);
        if (magnitudes.Length == 0)
        {
            return new Result { Status = "Error", Channel = channel, Reason = "the transform returned no bins" };
        }

        // The transform zero-pads to a power of two, so the bin width follows the padded length
        // rather than the sample count -- using the latter would shift every reported frequency.
        int transformSize = magnitudes.Length * 2;
        double binHz = rate / transformSize;

        var frequencies = new double[magnitudes.Length];
        for (int i = 0; i < magnitudes.Length; i++) frequencies[i] = i * binHz;

        int peak = -1;
        for (int i = 1; i < magnitudes.Length; i++)
        {
            if (peak < 0 || magnitudes[i] > magnitudes[peak]) peak = i;
        }

        return new Result
        {
            Channel = channel,
            Samples = taken,
            WindowSec = span,
            SampleRateHz = rate,
            NyquistHz = rate / 2.0,
            BinHz = binHz,
            Frequencies = frequencies,
            Magnitudes = magnitudes,
            PeakHz = peak > 0 ? frequencies[peak] : null,
            PeakMagnitude = peak > 0 ? magnitudes[peak] : null
        };
    }
}
