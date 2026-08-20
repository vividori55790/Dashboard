using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>
/// One detector as an operator described it in a file, before anything was constructed from it.
/// </summary>
/// <remarks>
/// A plain description rather than a builder, for the reason <c>JsonChannelMap</c> is written the
/// same way: which detectors watch which channels is deployment configuration, and a deployment
/// that needs a rebuild to add a detector will not get one. Every field has a default that is
/// defensible on its own, except <see cref="MaxRatePerSecond"/>, which has none — a rate limit is a
/// physical fact about the plant and there is no value worth guessing.
/// </remarks>
public sealed class DetectorSpec
{
    /// <summary>Which detector to build: <c>mad</c>, <c>ewma</c>, <c>rate</c> or <c>zscore</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>The operator's own name for this entry, prefixed onto every verdict it issues.</summary>
    public string? Label { get; init; }

    /// <summary>Channel patterns this detector watches. Empty matches nothing, never everything.</summary>
    public IReadOnlyList<string> Channels { get; init; } = Array.Empty<string>();

    /// <summary>Window or training length, in samples. Meaning depends on <see cref="Kind"/>.</summary>
    public int Window { get; init; } = 32;

    /// <summary>Score at or above which the detector flags. In the units that detector reports.</summary>
    public double Threshold { get; init; } = 3.5;

    /// <summary>EWMA smoothing factor, in (0, 1]. Smaller reacts more slowly and detects smaller shifts.</summary>
    public double Lambda { get; init; } = 0.2;

    /// <summary>Rate limit for <c>rate</c>, in channel units per second. Required for that kind.</summary>
    public double MaxRatePerSecond { get; init; }

    /// <summary>Longest interval that still describes a rate, for <c>rate</c>.</summary>
    public double MaxGapSeconds { get; init; } = RateOfChangeDetector.DefaultMaxGapSeconds;

    /// <summary>Nominal feed rate for <c>zscore</c>, which converts its trend into units per second.</summary>
    public double SampleRateHz { get; init; } = 20.0;
}

/// <summary>
/// The whole analytics configuration: which detectors run, and which external model is consulted.
/// </summary>
/// <remarks>
/// An empty configuration is valid and means the host judges with whatever is hardcoded on the
/// ingest path and nothing else — which is where this system started. Adding a file is what makes
/// it more than that.
/// </remarks>
public sealed class DetectorConfiguration
{
    /// <summary>Detectors to build, in file order.</summary>
    public IReadOnlyList<DetectorSpec> Detectors { get; init; } = Array.Empty<DetectorSpec>();

    /// <summary>The external model to consult, or null to consult none.</summary>
    public InferenceSpec? Inference { get; init; }

    /// <summary>A configuration that adds nothing.</summary>
    public static DetectorConfiguration None { get; } = new();

    /// <summary>True when the file asked for nothing at all.</summary>
    public bool IsEmpty => Detectors.Count == 0 && Inference is null;
}
