using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Resilience;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>
/// Several detectors over the same channel, with their answers kept apart.
/// </summary>
/// <remarks>
/// The panel deliberately does not combine verdicts into one. Any rule for merging them — worst
/// wins, majority, average — throws away the only thing that makes running several worth doing:
/// <em>which</em> detector saw it. "The robust detector flagged this and the EWMA did not" is a
/// diagnosis; "the system flagged this at 4.1" is a number with no argument behind it.
///
/// <para>Duplicate identities are refused at construction. Two detectors answering under one id
/// would make their results indistinguishable in a stored record, which is the failure this whole
/// abstraction exists to prevent.</para>
///
/// <para>A detector that throws is contained and counted as having withheld a verdict. One badly
/// configured detector must not take down the ingest path, and it must not silently disappear
/// either — the tally is where it surfaces.</para>
/// </remarks>
public sealed class DetectorPanel
{
    /// <summary>Ceiling on retained flags, which are read and sorted together.</summary>
    public const int MaxRetainedFlags = 1_000;

    private readonly IChannelDetector[] _detectors;
    private readonly DetectorTally[] _tallies;
    private readonly BoundedChannelRegistry<FlaggedVerdict> _flags = new(MaxRetainedFlags);

    public DetectorPanel(IEnumerable<IChannelDetector>? detectors)
    {
        _detectors = (detectors ?? Array.Empty<IChannelDetector>()).Where(d => d is not null).ToArray();

        string[] duplicates = _detectors
            .GroupBy(d => d.DetectorId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new ArgumentException(
                "Two detectors share an id, so their verdicts could not be told apart: "
                + string.Join(", ", duplicates), nameof(detectors));
        }

        _tallies = _detectors.Select(d => new DetectorTally(d.DetectorId)).ToArray();
    }

    /// <summary>A panel that judges nothing, for a host with no detectors configured.</summary>
    /// <remarks>
    /// An empty panel is a legitimate configuration and reports zero verdicts, not zero anomalies.
    /// Nothing downstream may read "no detector flagged anything" as "nothing was wrong".
    /// </remarks>
    public static DetectorPanel Empty { get; } = new(Array.Empty<IChannelDetector>());

    /// <summary>The detectors in the panel, in configuration order.</summary>
    public IReadOnlyList<IChannelDetector> Detectors => _detectors;

    /// <summary>What each detector did during the run.</summary>
    public IReadOnlyList<DetectorTally> Tallies => _tallies;

    /// <summary>True when no detector is configured.</summary>
    public bool IsEmpty => _detectors.Length == 0;

    /// <summary>
    /// Asks every detector that handles this channel, and returns their answers separately.
    /// </summary>
    /// <remarks>
    /// Detectors that do not handle the channel are absent from the result rather than present with
    /// a withheld verdict: "not configured for this channel" is a fact about the configuration, and
    /// mixing it into the per-sample answers would make an unwatched channel look examined.
    /// </remarks>
    public IReadOnlyList<DetectorVerdict> Evaluate(string channelName, double value, DateTime observedUtc)
    {
        if (_detectors.Length == 0) return Array.Empty<DetectorVerdict>();

        var verdicts = new List<DetectorVerdict>(_detectors.Length);

        for (int i = 0; i < _detectors.Length; i++)
        {
            IChannelDetector detector = _detectors[i];
            if (!detector.CanHandle(channelName)) continue;

            DetectorVerdict verdict;
            try
            {
                verdict = detector.Evaluate(channelName, value, observedUtc);
            }
            catch (Exception ex)
            {
                verdict = DetectorVerdict.NotJudged($"detector threw: {ex.GetType().Name}: {ex.Message}");
            }

            _tallies[i].Count(verdict);
            if (verdict is { HasVerdict: true, IsAnomaly: true })
            {
                // Keyed by channel and detector together, so two detectors flagging one channel are
                // both retained. Bounded, like every other per-channel store here: an unbounded
                // record of what went wrong is its own way of bringing a host down.
                _flags.Set(channelName + "|" + verdict.DetectorId,
                    new FlaggedVerdict(channelName, value, observedUtc, verdict));
            }

            verdicts.Add(verdict);
        }

        return verdicts;
    }

    /// <summary>The most recent flag per channel and detector, worst score first.</summary>
    /// <remarks>
    /// Scores from different detectors are on different scales, so the ordering is a convenience
    /// for a reader, not a ranking. <see cref="DetectorVerdict.ScoreKind"/> travels with each entry
    /// precisely so nobody reads a robust sigma of 4 and a model score of 0.9 as comparable.
    /// </remarks>
    public IReadOnlyList<FlaggedVerdict> RecentFlags =>
        _flags.Snapshot().OrderByDescending(f => f.Verdict.Score).ToList();

    /// <summary>Discards the retained flags.</summary>
    public void ClearRecentFlags() => _flags.Clear();

    /// <summary>Discards what every detector remembers about one channel.</summary>
    public void Reset(string channelName)
    {
        foreach (IChannelDetector detector in _detectors) detector.Reset(channelName);
    }

    /// <summary>Verdicts flagged as anomalies across every detector.</summary>
    public long AnomaliesFlagged => _tallies.Sum(t => t.Flagged);

    /// <summary>Verdicts actually reached across every detector.</summary>
    public long VerdictsReached => _tallies.Sum(t => t.Judged);

    /// <summary>Lines describing what each detector did, or an empty list when the panel is empty.</summary>
    public IReadOnlyList<string> Summary() =>
        _tallies.Select(t => t.Summary()).ToArray();
}
