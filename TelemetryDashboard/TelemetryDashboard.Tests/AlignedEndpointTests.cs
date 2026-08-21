using System;
using System.Linq;
using FluentAssertions;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Query;
using TelemetryDashboard.Core.Streaming;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// <c>/api/aligned</c>: several channels as they stood at one instant, and how each was obtained.
/// </summary>
/// <remarks>
/// Channels do not arrive together, so "what were the input and the output at the same moment" —
/// the question behind every efficiency and every ratio — has no answer in the raw stream. Reading
/// the latest of each is wrong by exactly the interval between them.
/// <para>
/// <c>TimeSyncJitterBuffer</c> could answer it from M1 and was constructed by nothing. Wiring it
/// meant fixing what it said first: it returned 0.0 for a node that had sent nothing, and clamped
/// silently to the nearest sample for any instant outside its buffer.
/// </para>
/// </remarks>
public class AlignedEndpointTests
{
    /// <summary>A channel sampled at a fixed rate, carrying a straight ramp.</summary>
    /// <remarks>A ramp makes interpolation checkable by hand: the value at t is t times the slope.</remarks>
    private static SeriesStore Ramp(string channel, double rateHz, int samples, double slope = 1.0)
    {
        var store = new SeriesStore(samplesPerChannel: Math.Max(samples, 64));
        for (int i = 0; i < samples; i++)
        {
            double t = i / rateHz;
            store.Append(channel, t * slope, t);
        }
        return store;
    }

    [Fact]
    public void AValueBetweenTwoSamplesIsInterpolatedAndSaysSo()
    {
        // Samples at 0.0 and 0.1 seconds carrying 0.0 and 10.0. Halfway is 5.0, and nothing
        // reported 5.0 — which is the fact the label has to carry.
        SeriesStore store = Ramp("ramp", rateHz: 10.0, samples: 40, slope: 100.0);

        AlignedEndpoint.Result result =
            AlignedEndpoint.Compute(store, new[] { "ramp" }, atSec: 0.05, windowSec: 30);

        AlignedEndpoint.ChannelAlignment ch = result.Channels.Single();
        ch.Value.Should().BeApproximately(5.0, 1e-6);
        ch.Kind.Should().Be(nameof(AlignmentKind.Interpolated));
        ch.AnswersTheInstant.Should().BeTrue();
        result.AnsweredTheInstant.Should().Be(1);
    }

    [Fact]
    public void EveryChannelIsAlignedToTheSameInstant()
    {
        // The property the endpoint exists for. Three ramps of different slopes, read at one
        // instant, must be in the same ratio as their slopes -- which is only true if they were
        // all evaluated at that instant rather than at whenever each last happened to arrive.
        var store = new SeriesStore(samplesPerChannel: 256);
        for (int i = 0; i < 60; i++)
        {
            double t = i / 10.0;

            // Deliberately staggered: each channel reports at a different offset inside the tick,
            // which is what makes "the latest of each" the wrong answer. Each value is a function
            // of the instant it is stamped with, so every channel's true value at time T is its
            // slope times T and the ratios are exact.
            //
            // A first version of this stamped the stagger but computed the value from the
            // unstaggered t, so c was 4 * (T - 0.07) rather than 4 * T and the ratio came out at
            // 3.907. The endpoint was right and the test's arithmetic was wrong -- worth recording,
            // because the tempting repair was to widen the tolerance until 3.907 counted as 4.
            store.Append("a", t * 1.0, t);
            store.Append("b", (t + 0.03) * 2.0, t + 0.03);
            store.Append("c", (t + 0.07) * 4.0, t + 0.07);
        }

        AlignedEndpoint.Result result =
            AlignedEndpoint.Compute(store, new[] { "a", "b", "c" }, atSec: 3.0, windowSec: 30);

        result.AnsweredTheInstant.Should().Be(3);

        double a = result.Channels.Single(c => c.Channel == "a").Value!.Value;
        double b = result.Channels.Single(c => c.Channel == "b").Value!.Value;
        double c2 = result.Channels.Single(c => c.Channel == "c").Value!.Value;

        a.Should().BeApproximately(3.0, 0.05);
        (b / a).Should().BeApproximately(2.0, 0.01, "b rises twice as fast as a");
        (c2 / a).Should().BeApproximately(4.0, 0.01, "c rises four times as fast as a");
    }

    [Fact]
    public void AValueHeldFromOutsideTheSamplesIsLabelledAndMeasured()
    {
        SeriesStore store = Ramp("ramp", rateHz: 10.0, samples: 20, slope: 1.0);
        double lastSampleAt = 19 / 10.0;

        AlignedEndpoint.Result result = AlignedEndpoint.Compute(
            store, new[] { "ramp" }, atSec: lastSampleAt + 2.0, windowSec: 30);

        AlignedEndpoint.ChannelAlignment ch = result.Channels.Single();
        ch.Kind.Should().Be(nameof(AlignmentKind.HeldAfter));
        ch.GapSec.Should().BeApproximately(2.0, 1e-6,
            "how far outside the samples the instant lies is what lets a caller reject it");
        ch.AnswersTheInstant.Should().BeFalse();
        result.AnsweredTheInstant.Should().Be(0,
            "a ratio built from a value held two seconds ago is not a ratio of anything");
    }

    [Fact]
    public void AChannelNobodyHasSentReportsNothingRatherThanZero()
    {
        SeriesStore store = Ramp("present", rateHz: 10.0, samples: 20);

        AlignedEndpoint.Result result =
            AlignedEndpoint.Compute(store, new[] { "present", "absent" }, atSec: 1.0, windowSec: 30);

        AlignedEndpoint.ChannelAlignment absent = result.Channels.Single(c => c.Channel == "absent");
        absent.Value.Should().BeNull("zero is a perfectly ordinary reading and must not stand in for silence");
        absent.Kind.Should().Be(nameof(AlignmentKind.None));
        absent.Samples.Should().Be(0);

        result.Channels.Single(c => c.Channel == "present").Value.Should().NotBeNull();
        result.AnsweredTheInstant.Should().Be(1, "one of the two answered, and the count says so");
    }

    [Fact]
    public void AnInstantWithNoSamplesAnywhereNearItIsRefusedRatherThanHeld()
    {
        // An hour past the last sample is not a held value, it is no value. The window bounds how
        // far a held answer may reach, which stops a stale reading being dressed as a current one.
        SeriesStore store = Ramp("ramp", rateHz: 10.0, samples: 20);

        AlignedEndpoint.Result result =
            AlignedEndpoint.Compute(store, new[] { "ramp" }, atSec: 3600.0, windowSec: 30);

        result.Channels.Single().Kind.Should().Be(nameof(AlignmentKind.None));
        result.Channels.Single().Value.Should().BeNull();
    }

    [Fact]
    public void NamingNoChannelsIsAnErrorRatherThanAnEmptySuccess()
    {
        AlignedEndpoint.Result result =
            AlignedEndpoint.Compute(new SeriesStore(), Array.Empty<string>(), 1.0, 30);

        result.Status.Should().Be("Error");
        result.Reason.Should().Contain("channels");
    }
}
