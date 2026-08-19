using System;
using System.Collections.Generic;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Recording;

namespace TelemetryDashboard.UI.Diagnostics;

/// <summary>
/// The single source of clearly-labelled demonstration payloads.
/// </summary>
/// <remarks>
/// Some operator actions legitimately need synthetic data — proving a Slack webhook is reachable
/// requires sending something, and no live incident may be available. Those payloads all live here
/// so a literal anomaly score anywhere else in the application is unambiguously a defect, which is
/// what the architecture rule checks for.
/// <para>
/// Every payload announces itself. A test alert arriving in an on-call channel must be impossible
/// to mistake for a real one.
/// </para>
/// </remarks>
public static class DemonstrationPayloads
{
    /// <summary>Prefix applied to any synthetic payload leaving the application.</summary>
    public const string Marker = "[TEST]";

    /// <summary>A synthetic thermal excursion used to verify alert delivery.</summary>
    public static AnomalyResult AlertDeliveryProbe(string channelName) => new()
    {
        ChannelName = $"{Marker} {channelName}",
        CurrentValue = 102.1,
        ZScore = 4.1,
        IsAnomaly = true,
        Mean = 25.5,
        StdDev = 1.2,
        PredictedValueIn60s = 120.0,
        EstimatedTimeToBreachSec = 14.5
    };

    /// <summary>Waveform accompanying <see cref="AlertDeliveryProbe"/>.</summary>
    public static List<double> AlertDeliveryWaveform() =>
        new() { 25.0, 25.2, 26.0, 35.0, 78.4, 94.6, 102.1 };

    /// <summary>Diagnosis text making the synthetic origin explicit to the recipient.</summary>
    public const string AlertDeliveryDiagnosis =
        Marker + " Connectivity check from TelemetryDashboard. This is not a live incident. " +
        "Sample scenario: rapid thermal climb above 100°C with a positive slope projection.";

    /// <summary>
    /// Fallback anomaly set for the diagnosis dialog when the engine has recorded nothing yet.
    /// </summary>
    public static IReadOnlyList<AnomalyResult> DiagnosisFallback() => new List<AnomalyResult>
    {
        new() { ChannelName = $"{Marker} temp_node1", CurrentValue = 94.6, ZScore = 3.8, IsAnomaly = true, PredictedValueIn60s = 118.2, EstimatedTimeToBreachSec = 14.5 },
        new() { ChannelName = $"{Marker} vib_node1", CurrentValue = 4.2, ZScore = 3.2, IsAnomaly = true, PredictedValueIn60s = 5.8, EstimatedTimeToBreachSec = 22.0 },
        new() { ChannelName = $"{Marker} volt_bus", CurrentValue = 20.4, ZScore = 2.9, IsAnomaly = true, PredictedValueIn60s = 18.0, EstimatedTimeToBreachSec = 35.0 }
    };

    /// <summary>
    /// Analyzer identity stamped on every frame <see cref="SeedDvrTimeline"/> writes.
    /// </summary>
    /// <remarks>
    /// The sigma values in the seeded timeline were authored, not computed, so no analyzer can
    /// honestly be named for them. A dedicated id keeps the frames usable as verdicts — the DVR
    /// grid and the incident report both key off <c>HasVerdict</c> — while making their scripted
    /// origin visible anywhere provenance is shown. It carries <see cref="Marker"/> and cannot
    /// collide with the identifiers <see cref="TelemetryMlAnalyticsEngine"/> emits.
    /// </remarks>
    public const string DemonstrationAnalyzerId = Marker + " scripted-demonstration";

    /// <summary>Span of the seeded timeline, matching the replay dialog's scrub range.</summary>
    private const int TimelineSpanSec = 60;

    /// <summary>Seconds-ago bounds of the scripted excursion; the dialog's jump button targets -18s.</summary>
    private const int ExcursionStartSecAgo = 15;
    private const int ExcursionEndSecAgo = 20;

    /// <summary>
    /// Fills an empty DVR timeline with a scripted incident so the replay dialog has something to
    /// scrub through on a bench with no hardware attached. Does nothing once the player holds frames.
    /// </summary>
    /// <remarks>
    /// This seeding used to live in the replay dialog under the bare channel names
    /// <c>temp_node1</c>, <c>vib_node1</c> and <c>volt_bus</c>, which are also plausible names for
    /// real hardware — an operator scrubbing back through an authored thermal runaway had nothing
    /// on screen telling them so. Every channel now carries <see cref="Marker"/> and every frame
    /// <see cref="DemonstrationAnalyzerId"/>.
    /// <para>
    /// Frames are stamped at their intended position on the timeline rather than all at the current
    /// instant, which is what the previous loop did: sixty samples landing within a few milliseconds
    /// collapsed the whole "sixty second" timeline into one point, so scrubbing showed the same
    /// frame everywhere and the excursion could never be reached.
    /// </para>
    /// </remarks>
    /// <param name="dvr">Timeline to fill; ignored when null or already populated.</param>
    public static void SeedDvrTimeline(TimeTravelDvrPlayer? dvr)
    {
        if (dvr is null || dvr.FrameCount > 0) return;

        double nowSec = DateTime.UtcNow.Ticks / 10_000_000.0;

        for (int secondsAgo = TimelineSpanSec; secondsAgo >= 0; secondsAgo--)
        {
            double at = nowSec - secondsAgo;
            bool spike = secondsAgo >= ExcursionStartSecAgo && secondsAgo <= ExcursionEndSecAgo;

            double temperature = spike
                ? 96.5 + (ExcursionEndSecAgo - secondsAgo) * 1.5
                : 25.0 + Math.Sin(secondsAgo * 0.2) * 2.0;

            Seed(dvr, "temp_node1", temperature, spike ? 3.9 : 0.3, spike, at);
            Seed(dvr, "vib_node1", spike ? 4.5 : 0.25, spike ? 3.4 : 0.2, spike, at);
            Seed(dvr, "volt_bus", spike ? 19.8 : 24.0, spike ? 2.8 : 0.1, spike, at);
        }
    }

    private static void Seed(TimeTravelDvrPlayer dvr, string channel, double value, double sigma, bool isAnomaly, double atSec) =>
        dvr.RecordFrame($"{Marker} {channel}", value, sigma, isAnomaly, atSec, DemonstrationAnalyzerId);
}
