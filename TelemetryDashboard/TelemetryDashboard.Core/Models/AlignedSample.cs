using System;

namespace TelemetryDashboard.Core.Models;

/// <summary>How a channel's value at a requested instant was arrived at.</summary>
/// <remarks>
/// The distinction is the whole point. Two of these are answers about the instant asked for and two
/// are not, and a caller that cannot tell them apart will plot a held value as a measurement.
/// </remarks>
public enum AlignmentKind
{
    /// <summary>Nothing to align: the node is unknown, or it has sent nothing.</summary>
    None,

    /// <summary>A sample sits exactly at the requested instant.</summary>
    Exact,

    /// <summary>Between two samples, and interpolated from them.</summary>
    Interpolated,

    /// <summary>Earlier than anything received. The first sample's value is reported, unchanged.</summary>
    HeldBefore,

    /// <summary>Later than anything received. The last sample's value is reported, unchanged.</summary>
    HeldAfter
}

/// <summary>
/// A channel's value at a requested instant, and how honestly that value answers the question.
/// </summary>
/// <remarks>
/// What this replaces was a bare <c>double</c>, and it could not say three different things:
/// <list type="bullet">
/// <item>"this node has sent nothing" came back as <c>0.0</c>, which is also a perfectly ordinary
/// reading — so an unwired node and a node reading zero volts were the same answer.</item>
/// <item>A request for an instant an hour past the last sample returned that sample, silently. A
/// clamp presented as an alignment; the caller had no way to know the value was stale.</item>
/// <item>Interpolated and measured values were indistinguishable, so a chart built from them
/// carried invented points that looked exactly like recorded ones.</item>
/// </list>
/// A test in this repository asserted the first of those as the required behaviour, which is how
/// it survived: <c>GetAlignedSample(node, 10.0).Should().Be(0.0)</c> on an empty buffer.
/// </remarks>
/// <param name="Value">The value, or <see cref="double.NaN"/> when <paramref name="Kind"/> is None.</param>
/// <param name="Kind">How the value was obtained.</param>
/// <param name="GapSec">
/// For a held value, how far the requested instant lies outside the samples, in seconds. Zero
/// otherwise. This is what lets a caller decide whether a held value is close enough to use.
/// </param>
public readonly record struct AlignedSample(double Value, AlignmentKind Kind, double GapSec)
{
    /// <summary>Nothing to report.</summary>
    public static AlignedSample None { get; } = new(double.NaN, AlignmentKind.None, 0.0);

    /// <summary>Whether the value describes the instant that was asked about.</summary>
    /// <remarks>
    /// A held value describes a different instant, and saying so is the difference between a gap in
    /// a chart and a flat line that reads as a measurement.
    /// </remarks>
    public bool AnswersTheInstant => Kind is AlignmentKind.Exact or AlignmentKind.Interpolated;

    /// <summary>Whether there is any value at all.</summary>
    public bool HasValue => Kind != AlignmentKind.None;
}
