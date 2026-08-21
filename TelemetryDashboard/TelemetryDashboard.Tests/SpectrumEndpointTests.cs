using System;
using System.Linq;
using FluentAssertions;
using TelemetryDashboard.Core.Query;
using TelemetryDashboard.Core.Streaming;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// <c>/api/spectrum</c>: the frequency content of one channel, and what it refuses to guess.
/// </summary>
/// <remarks>
/// <c>FftAnalyzerService</c> was written in M2 and lived in the WPF view-model folder, which the
/// headless host is forbidden to reference — so the one place a spectrum reaches every client, an
/// endpoint any browser can call, could not have it. Moving it to Core and putting an endpoint in
/// front of it made it reachable, and its first run against live data found a defect in the series
/// store: every channel of a profile was being written into one series.
/// </remarks>
public class SpectrumEndpointTests
{
    /// <summary>Fills a store with a sine of a known frequency, sampled at a known rate.</summary>
    private static SeriesStore Sine(
        string channel, double hz, double rateHz, int samples, double offset = 0.0, double amplitude = 1.0)
    {
        var store = new SeriesStore(samplesPerChannel: Math.Max(samples, 64));
        for (int i = 0; i < samples; i++)
        {
            double t = i / rateHz;
            store.Append(channel, offset + amplitude * Math.Sin(2 * Math.PI * hz * t), t);
        }
        return store;
    }

    [Fact]
    public void AKnownSineIsFoundAtItsKnownFrequency()
    {
        const double rate = 100.0, hz = 7.0;
        SeriesStore store = Sine("rig.vibration", hz, rate, samples: 1024);

        SpectrumEndpoint.Result result = SpectrumEndpoint.Compute(store, "rig.vibration", 20.0, nowSec: 1024 / rate);

        result.Status.Should().Be("Success", result.Reason);
        result.PeakHz.Should().NotBeNull();

        // Within one bin. Asserting an exact frequency would be asserting that the signal landed
        // on a bin centre, which is a property of the numbers chosen rather than of the transform.
        Math.Abs(result.PeakHz!.Value - hz).Should().BeLessThan(result.BinHz * 1.5,
            $"a {hz} Hz sine has to appear at {hz} Hz, not near it by luck");
    }

    [Fact]
    public void TheSampleRateIsMeasuredFromTheTimestampsRatherThanAssumed()
    {
        // Two stores holding the same waveform sampled at different rates must report different
        // rates and the same frequency. A telemetry stream never arrives on a metronome, and a
        // spectrum labelled with an assumed rate puts every peak in the wrong place while looking
        // entirely plausible.
        SpectrumEndpoint.Result fast =
            SpectrumEndpoint.Compute(Sine("c", 5.0, 200.0, 1024), "c", 20.0, 1024 / 200.0);
        SpectrumEndpoint.Result slow =
            SpectrumEndpoint.Compute(Sine("c", 5.0, 50.0, 1024), "c", 40.0, 1024 / 50.0);

        fast.SampleRateHz.Should().BeApproximately(200.0, 1.0);
        slow.SampleRateHz.Should().BeApproximately(50.0, 1.0);

        Math.Abs(fast.PeakHz!.Value - 5.0).Should().BeLessThan(fast.BinHz * 1.5);
        Math.Abs(slow.PeakHz!.Value - 5.0).Should().BeLessThan(slow.BinHz * 1.5);
    }

    [Fact]
    public void ALargeConstantOffsetDoesNotHideTheSignalRidingOnIt()
    {
        // A 400 V bus with a 2 V ripple is the ordinary case, not the exotic one. Leaving the mean
        // in puts nearly all the energy in bin 0 and scales the ripple into invisibility beside it.
        SeriesStore store = Sine("dab.bus_voltage", 3.0, 100.0, 1024, offset: 400.0, amplitude: 2.0);

        SpectrumEndpoint.Result result = SpectrumEndpoint.Compute(store, "dab.bus_voltage", 20.0, 1024 / 100.0);

        result.Status.Should().Be("Success", result.Reason);
        Math.Abs(result.PeakHz!.Value - 3.0).Should().BeLessThan(result.BinHz * 1.5);
    }

    [Fact]
    public void TheDcBinIsNeverReportedAsThePeak()
    {
        // DC is the mean, which is the largest bin for almost every telemetry channel. Calling it
        // "the peak" would make every spectrum look like it had found a periodicity.
        var store = new SeriesStore();
        for (int i = 0; i < 256; i++) store.Append("flat", 42.0, i / 10.0);

        SpectrumEndpoint.Result result = SpectrumEndpoint.Compute(store, "flat", 60.0, 25.6);

        result.Status.Should().Be("Success", result.Reason);
        result.PeakHz.Should().NotBe(0.0, "bin zero is the mean, not a frequency the signal contains");
    }

    [Fact]
    public void AChannelNobodyHasSentIsRefusedByName()
    {
        SpectrumEndpoint.Result result =
            SpectrumEndpoint.Compute(new SeriesStore(), "never.arrived", 60.0, 100.0);

        result.Status.Should().Be("Error");
        result.Reason.Should().Contain("never.arrived");
        result.Magnitudes.Should().BeEmpty("an empty spectrum is not a spectrum of silence");
    }

    [Fact]
    public void TooFewSamplesIsSaidRatherThanTransformed()
    {
        var store = new SeriesStore();
        for (int i = 0; i < 4; i++) store.Append("young", i, i / 10.0);

        SpectrumEndpoint.Result result = SpectrumEndpoint.Compute(store, "young", 60.0, 0.4);

        result.Status.Should().Be("Error");
        result.Reason.Should().Contain("4 sample");
        result.PeakHz.Should().BeNull(
            "below a handful of samples the bins are wider than anything they could resolve, and a "
            + "spectrum returned anyway invites a reader to point at an artefact of the window");
    }

    [Fact]
    public void SamplesSharingOneTimestampYieldNoRateAndNoSpectrum()
    {
        var store = new SeriesStore();
        for (int i = 0; i < 64; i++) store.Append("frozen", Math.Sin(i), 5.0);

        SpectrumEndpoint.Result result = SpectrumEndpoint.Compute(store, "frozen", 60.0, 5.0);

        result.Status.Should().Be("Error");
        result.Reason.Should().Contain("same timestamp");
    }

    [Fact]
    public void EveryMagnitudeHasAFrequencyBesideIt()
    {
        SpectrumEndpoint.Result result =
            SpectrumEndpoint.Compute(Sine("c", 4.0, 64.0, 512), "c", 20.0, 8.0);

        result.Frequencies.Should().HaveSameCount(result.Magnitudes,
            "a magnitude with no frequency is a number a reader cannot place");
        result.Frequencies.Should().BeInAscendingOrder();
        result.NyquistHz.Should().BeApproximately(result.SampleRateHz / 2.0, 1e-9);
    }
}
