using System.Globalization;
using TelemetryDashboard.Core.Analytics.Detectors;

namespace TelemetryDashboard.Infrastructure.Analytics;

/// <summary>
/// The half of <see cref="RemoteInferenceDetector"/> that decides whether the model's newest answer
/// may still be quoted about the sample in hand.
/// </summary>
/// <remarks>
/// Separated because it is the part worth reading. Everything else here is plumbing; this is where
/// a score that did arrive is either turned into a verdict or refused because it has aged out —
/// which is the one rule standing between "the model went down" and a dashboard that keeps
/// displaying the last thing the model said before it did.
/// </remarks>
public sealed partial class RemoteInferenceDetector
{
    /// <summary>Reports the newest answer, or explains why there is not one worth reporting.</summary>
    private DetectorVerdict Quote(InferenceChannelState state)
    {
        InferenceScore? latest = state.Latest;
        if (latest is null)
        {
            return DetectorVerdict.NotJudged(
                "the model has not returned a usable score for this channel; nothing is being judged",
                state.Window.Count, 1.0);
        }

        double ageMs = (_clock() - latest.ReceivedUtc).TotalMilliseconds;
        if (ageMs > _spec.MaxScoreAgeMs)
        {
            // The whole point of the age limit. A score the model produced before it stopped
            // answering describes a window that has since scrolled off; quoting it now would report
            // a judgement about data the model never saw.
            Tally.CountStale();

            string stale = string.Create(CultureInfo.InvariantCulture,
                $"newest score is {ageMs:0} ms old, past the {_spec.MaxScoreAgeMs} ms limit");

            return DetectorVerdict.NotJudged(
                stale + "; the model has stopped keeping up", state.Window.Count, 1.0);
        }

        // A model that states its own verdict is obeyed; one that returns only a number is measured
        // against the operator's threshold. Which of the two happened travels in the reason, because
        // the same score can mean different things under the two rules.
        bool isAnomaly = latest.ModelJudgement ?? latest.Score >= _spec.Threshold;
        string basis = latest.ModelJudgement is null
            ? "host threshold " + _spec.Threshold.ToString("0.###", CultureInfo.InvariantCulture)
            : "the model's own verdict";

        string reason = string.Create(CultureInfo.InvariantCulture,
            $"model score {latest.Score:0.###} by {basis}, from a window ending {latest.WindowEndUtc:HH:mm:ss.fff}Z, {ageMs:0} ms ago");

        return DetectorVerdict.Judged(
            DetectorId, isAnomaly, latest.Score, DetectorScoreKind.ModelScore, 1.0, state.Window.Count, reason);
    }

    /// <summary>Stores an answer, or records that one is no longer outstanding. Runs on the pump.</summary>
    /// <remarks>
    /// A null score is not stored, which is what makes the failure path silent rather than
    /// destructive: an endpoint that starts returning rubbish neither overwrites the last good score
    /// nor invents a new one. It simply stops refreshing, and the age limit above retires the old
    /// answer on schedule.
    /// </remarks>
    private void OnScored(InferenceRequest request, InferenceScore? score)
    {
        if (!_states.TryGet(request.Channel, out InferenceChannelState? state) || state is null) return;

        lock (state)
        {
            state.InFlight = false;
            if (score is not null) state.Latest = score;
        }
    }
}
