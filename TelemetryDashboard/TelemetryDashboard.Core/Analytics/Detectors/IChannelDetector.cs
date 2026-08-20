using System;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>
/// One way of judging a telemetry channel. The engine is an implementation of this, not the only
/// way to detect anything.
/// </summary>
/// <remarks>
/// The contract exists because a single hardcoded detector is a claim that one statistic answers
/// every question a plant floor asks. A rolling z-score is wrecked by a single outlier entering its
/// own baseline, cannot see a level shift once the shift fills its window, and needs a scale that a
/// perfectly flat channel does not have. Those are not defects to fix; they are what that detector
/// is. The fix is to be able to run another one beside it and tell the two answers apart.
///
/// <para><b>Every implementation must be able to decline.</b> <see cref="Evaluate"/> returns
/// <see cref="DetectorVerdict.NotJudged"/> whenever the detector lacks what it needs — during
/// warm-up, on a baseline with no measurable scale, or when an external model did not answer. A
/// detector that always produces a number is a detector that will produce one when it knows
/// nothing, and that number will be believed.</para>
///
/// <para><b>Implementations must not block.</b> <see cref="Evaluate"/> runs on the ingest path.
/// A detector that reaches a network does so through a bounded hand-off and reports on the last
/// answer that came back, never by waiting for the next one.</para>
/// </remarks>
public interface IChannelDetector
{
    /// <summary>
    /// Identifies this detector and the settings behind its verdicts.
    /// </summary>
    /// <remarks>
    /// Two detectors of the same kind with different windows disagree about identical input, so the
    /// settings are part of the identity. This is what makes several detectors over one channel
    /// distinguishable after the fact, and what lets a disputed number be traced back to the
    /// configuration that produced it.
    /// </remarks>
    string DetectorId { get; }

    /// <summary>Whether this detector is configured to judge <paramref name="channelName"/> at all.</summary>
    /// <remarks>
    /// A detector that does not handle a channel produces no verdict for it and no state for it.
    /// This is separate from declining to judge: "not my channel" and "my channel, not enough data"
    /// are different facts and the panel reports them differently.
    /// </remarks>
    bool CanHandle(string channelName);

    /// <summary>
    /// Judges one sample, or declines to.
    /// </summary>
    /// <param name="channelName">Fully qualified channel, e.g. <c>NODE_1.TEMP</c>.</param>
    /// <param name="value">The measured value. Non-finite input must be declined, never scored.</param>
    /// <param name="observedUtc">When the sample was observed, for detectors that measure per unit time.</param>
    DetectorVerdict Evaluate(string channelName, double value, DateTime observedUtc);

    /// <summary>Discards whatever this detector remembers about one channel.</summary>
    void Reset(string channelName);
}
