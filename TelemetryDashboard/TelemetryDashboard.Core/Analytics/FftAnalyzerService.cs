using System;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// Turns a block of time-domain samples into a magnitude spectrum.
/// </summary>
/// <remarks>
/// The transform is written out here rather than taken from a DSP package. The scope needs exactly
/// one real-input forward FFT and nothing else, and a dependency whose surface is a hundred times
/// larger than the use made of it is a liability every upgrade cycle.
/// <para>
/// It lived in <c>UI/ViewModels</c>, which is why nothing could reach it: the headless host must
/// never reference the WPF project, so the one place a spectrum is useful to every client — an
/// endpoint any browser can call — could not have it. A Fourier transform is an analytics concern
/// and its address was the mistake, not its contents.
/// </para>
/// <para>
/// Only the lower half of the spectrum is returned. The input is real, so the upper half is its
/// complex-conjugate mirror and carries no information the lower half does not already hold.
/// </para>
/// </remarks>
public sealed class FftAnalyzerService
{
    /// <summary>Smallest transform the analyser will run.</summary>
    /// <remarks>A one-point transform has no bins to report, so a lone sample is padded to two.</remarks>
    private const int MinimumTransformSize = 2;

    /// <summary>
    /// Computes the half magnitude spectrum of <paramref name="samples"/>.
    /// </summary>
    /// <remarks>
    /// Input is zero-padded up to the next power of two. Padding is interpolation rather than new
    /// information — it subdivides the bins without improving true resolution — which is precisely
    /// what a scope wants, since the alternative is a trace that changes width with buffer fill.
    /// Non-finite samples are substituted with zero, because a single NaN propagates through every
    /// butterfly and would blank the entire display rather than the one bad bin. Magnitudes are
    /// divided by the transform size so a constant input of amplitude A reads A in the DC bin
    /// whatever the buffer length, keeping the vertical scale stable as the window grows.
    /// </remarks>
    public double[] ComputeFft(double[] samples)
    {
        if (samples is null || samples.Length == 0) return Array.Empty<double>();

        int size = NextPowerOfTwo(Math.Max(samples.Length, MinimumTransformSize));
        double[] real = new double[size];
        double[] imaginary = new double[size];

        for (int i = 0; i < samples.Length && i < size; i++)
        {
            real[i] = double.IsFinite(samples[i]) ? samples[i] : 0.0;
        }

        Transform(real, imaginary);

        double[] magnitudes = new double[size / 2];
        for (int bin = 0; bin < magnitudes.Length; bin++)
        {
            magnitudes[bin] = Math.Sqrt(real[bin] * real[bin] + imaginary[bin] * imaginary[bin]) / size;
        }
        return magnitudes;
    }

    /// <summary>
    /// Highest frequency a spectrum sampled at <paramref name="samplingRate"/> can represent.
    /// </summary>
    /// <remarks>
    /// This is the Nyquist limit, which depends only on how fast the signal was sampled.
    /// <paramref name="fftSize"/> decides how finely that fixed span is divided into bins, never
    /// where it ends; it is accepted so call sites read as one unit with the transform they label.
    /// </remarks>
    public double GetMaxFrequency(double samplingRate, int fftSize)
    {
        return double.IsFinite(samplingRate) && samplingRate > 0.0 ? samplingRate / 2.0 : 0.0;
    }

    /// <summary>
    /// In-place iterative radix-2 Cooley-Tukey transform. Array length must be a power of two.
    /// </summary>
    /// <remarks>
    /// Iterative rather than recursive: the recursive form allocates two half-length arrays per
    /// level, which at a few thousand points and sixty frames a second is megabytes of short-lived
    /// garbage every second, all of it on the render path.
    /// </remarks>
    private static void Transform(double[] real, double[] imaginary)
    {
        int n = real.Length;
        BitReversePermute(real, imaginary);

        for (int span = 2; span <= n; span <<= 1)
        {
            double angleStep = -2.0 * Math.PI / span;
            int half = span / 2;

            for (int block = 0; block < n; block += span)
            {
                for (int k = 0; k < half; k++)
                {
                    double angle = angleStep * k;
                    double cos = Math.Cos(angle);
                    double sin = Math.Sin(angle);

                    int even = block + k;
                    int odd = even + half;

                    double twiddledReal = real[odd] * cos - imaginary[odd] * sin;
                    double twiddledImaginary = real[odd] * sin + imaginary[odd] * cos;

                    real[odd] = real[even] - twiddledReal;
                    imaginary[odd] = imaginary[even] - twiddledImaginary;
                    real[even] += twiddledReal;
                    imaginary[even] += twiddledImaginary;
                }
            }
        }
    }

    /// <summary>
    /// Reorders samples into bit-reversed index order.
    /// </summary>
    /// <remarks>
    /// This is what lets the butterflies run in place: after the permutation, every pair a stage
    /// needs is already adjacent, so no stage has to shuffle data between two buffers.
    /// </remarks>
    private static void BitReversePermute(double[] real, double[] imaginary)
    {
        int n = real.Length;

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }
            j |= bit;

            if (i >= j) continue;
            (real[i], real[j]) = (real[j], real[i]);
            (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
        }
    }

    private static int NextPowerOfTwo(int value)
    {
        int size = MinimumTransformSize;
        while (size < value)
        {
            size <<= 1;
        }
        return size;
    }
}
