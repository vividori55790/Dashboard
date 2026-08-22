using System;
using System.Linq;
using FluentAssertions;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Query;
using TelemetryDashboard.Core.Streaming;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// A known waveform, so the spectrum can be checked rather than trusted.
/// </summary>
/// <remarks>
/// <c>SignalGeneratorService</c> had been written, tested and constructed by nothing. What its
/// absence cost was not a feature but a <em>reference</em>: the simulator emits one shape per
/// channel at a period derived from a hash, so nothing could tell an operator whether the peak
/// <c>/api/spectrum</c> draws is the frequency the channel is oscillating at. The evidence was that
/// the number looked plausible.
/// <para>
/// Measured on a live host once this existed: a declared 2 Hz sine came back as 2.0103 Hz, within
/// 0.55 of the endpoint's own bin width. The first run of it read 1.8883 Hz — six bins out — and
/// the analyser was right: the generator had been advancing its phase by the interval it asked for
/// rather than the time that actually passed, and the simulator ticks at 9.5 Hz against a nominal
/// 10. A reference that is wrong is worse than no reference, so that is fixed at the cause and
/// pinned below.
/// </para>
/// </remarks>
public class InjectedSignalTests
{
    [Theory]
    [InlineData("dab.bus_voltage=sine@2:20", "dab.bus_voltage", WaveformType.Sine, 2.0, 20.0)]
    [InlineData("a.b=SQUARE@0.5:3", "a.b", WaveformType.Square, 0.5, 3.0)]
    [InlineData("a.b = triangle @ 1.5 : 0.25", "a.b", WaveformType.Triangle, 1.5, 0.25)]
    [InlineData("a.b=sawtooth@4", "a.b", WaveformType.Sawtooth, 4.0, 1.0)]
    public void ADeclarationCarriesItsChannelShapeRateAndAmplitude(
        string declaration, string channel, WaveformType shape, double hz, double amplitude)
    {
        InjectedSignal signal = InjectedSignal.Parse(declaration);

        signal.Channel.Should().Be(channel);
        signal.Shape.Should().Be(shape);
        signal.FrequencyHz.Should().Be(hz);
        signal.Amplitude.Should().Be(amplitude);
    }

    [Theory]
    [InlineData("", "for example")]
    [InlineData("nope", "is not a signal")]
    [InlineData("a.b=sawwave@2:5", "is not a waveform")]
    [InlineData("a.b=sine@0:5", "needs a positive rate")]
    [InlineData("a.b=sine@2:0", "a signal with no amplitude is not one")]
    public void AMalformedDeclarationIsRefusedWhereItIsWritten(string declaration, string expected)
    {
        Action parse = () => InjectedSignal.Parse(declaration);

        parse.Should().Throw<FormatException>().WithMessage($"*{expected}*");
    }

    [Fact]
    public void AnUnknownShapeIsRefusedRatherThanQuietlyBecomingASine()
    {
        // The tempting default, and wrong here more than anywhere: a misspelled shape that becomes
        // a sine produces a spectrum that looks right for the wrong reason, and this feature exists
        // to be the thing other measurements are checked against.
        Action parse = () => InjectedSignal.Parse("a.b=sinusoid@2:1");

        parse.Should().Throw<FormatException>().WithMessage("*sine, square, triangle, sawtooth, noise*");
    }

    [Theory]
    [InlineData(2.0, 10.0, false)]
    [InlineData(4.9, 10.0, false)]
    [InlineData(5.1, 10.0, true)]
    [InlineData(8.0, 10.0, true)]
    public void ARateAboveNyquistIsReportedAsFoldingBack(double hz, double sampleRate, bool aliases)
    {
        InjectedSignal.Parse($"a.b=sine@{hz}:1").AliasesAt(sampleRate).Should().Be(aliases);
    }

    // ---- the property the whole feature exists for --------------------------

    /// <summary>
    /// Generates a signal at a stated rate and asks the spectrum endpoint what it sees.
    /// </summary>
    /// <remarks>
    /// The same arithmetic the live host does, without the process: the generator's own samples go
    /// into a series store and the endpoint reads them back. Deterministic, because the sample
    /// spacing here is exact rather than whatever the scheduler managed.
    /// </remarks>
    private static SpectrumEndpoint.Result Measure(
        string declaration, double sampleRateHz, int samples)
    {
        InjectedSignal signal = InjectedSignal.Parse(declaration);
        SignalGeneratorService generator = signal.Arm();

        var store = new SeriesStore();
        double dt = 1.0 / sampleRateHz;
        double t0 = 1_000_000.0;

        for (int i = 0; i < samples; i++)
        {
            store.Append("N.a.b", 400 + generator.GetNextSample(dt), t0 + i * dt);
        }

        return SpectrumEndpoint.Compute(store, "N.a.b", samples * dt + 1.0, t0 + samples * dt);
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(1.0)]
    [InlineData(0.5)]
    [InlineData(3.25)]
    public void TheSpectrumReportsTheFrequencyThatWasAskedFor(double hz)
    {
        SpectrumEndpoint.Result result = Measure($"a.b=sine@{hz}:20", sampleRateHz: 20.0, samples: 2048);

        result.Status.Should().Be("Success");
        result.PeakHz.Should().NotBeNull();

        double error = Math.Abs(result.PeakHz!.Value - hz);
        error.Should().BeLessThan(result.BinHz,
            $"a {hz} Hz reference came back as {result.PeakHz} Hz, which is "
            + $"{error / result.BinHz:F2} of the endpoint's own bin width");
    }

    [Fact]
    public void ASquareWavePutsItsThirdHarmonicWhereTheoryDoes()
    {
        // A second, independent check on the analyser: a square carries odd harmonics at 1/n. The
        // live run measured the third at 0.328 of the fundamental against a theoretical 1/3.
        SpectrumEndpoint.Result result = Measure("a.b=square@1:20", sampleRateHz: 64.0, samples: 4096);

        double Magnitude(double near) => result.Frequencies
            .Select((f, i) => (f, m: result.Magnitudes[i]))
            .Where(x => Math.Abs(x.f - near) < 0.05)
            .Max(x => x.m);

        double fundamental = Magnitude(1.0);
        (Magnitude(3.0) / fundamental).Should().BeApproximately(1.0 / 3.0, 0.05);
        (Magnitude(5.0) / fundamental).Should().BeApproximately(1.0 / 5.0, 0.05);

        (Magnitude(2.0) / fundamental).Should().BeLessThan(0.05,
            "a square wave has no even harmonics");
    }

    [Fact]
    public void ASignalRidesOnTheSetpointRatherThanReplacingIt()
    {
        // A deviation, so the channel still reads as itself and moving the setpoint moves the whole
        // waveform with it. An absolute waveform would mean every declaration had to know the
        // channel's operating point.
        SignalGeneratorService generator = InjectedSignal.Parse("a.b=sine@1:20").Arm();

        double[] samples = Enumerable.Range(0, 64)
            .Select(_ => 400 + generator.GetNextSample(1.0 / 64.0))
            .ToArray();

        samples.Max().Should().BeApproximately(420, 1.0);
        samples.Min().Should().BeApproximately(380, 1.0);
        samples.Average().Should().BeApproximately(400, 1.0);
    }
}
