using System.Globalization;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>What the number in <see cref="DetectorVerdict.Score"/> is measured in.</summary>
/// <remarks>
/// Carried with the score because detectors do not share a scale. Three sigma, a rate of three
/// units per second and a model probability of three-tenths are all "3-ish" to a caller that reads
/// only the number, and a dashboard that puts them in one column invents a comparison nobody made.
/// </remarks>
public enum DetectorScoreKind
{
    /// <summary>No score, because no verdict was reached.</summary>
    None,

    /// <summary>Standard deviations from a mean baseline.</summary>
    Sigma,

    /// <summary>Deviations from a median baseline, scaled by MAD so the unit matches sigma on normal data.</summary>
    RobustSigma,

    /// <summary>Change in channel units per second.</summary>
    UnitsPerSecond,

    /// <summary>A model's own output, on whatever scale the model was trained to emit.</summary>
    ModelScore
}

/// <summary>
/// One detector's answer about one sample, including the answer "I did not judge this".
/// </summary>
/// <remarks>
/// <see cref="DetectorId"/> is null exactly when no judgement was reached, mirroring
/// <see cref="AnomalyResult.AnalyzerId"/> and for the same reason: a detector still warming up
/// leaves <see cref="Score"/> at 0 and <see cref="IsAnomaly"/> false, which reads identically to a
/// channel that was examined and found calm. Callers must check <see cref="HasVerdict"/> before
/// rendering either field.
/// <para>
/// <see cref="Reason"/> is populated in both directions. "Nothing is wrong" and "I could not tell"
/// are the two answers an operator must never confuse, so the second one says why.
/// </para>
/// </remarks>
/// <param name="DetectorId">Detector and settings behind the verdict, or null when none was reached.</param>
/// <param name="IsAnomaly">Whether this detector flagged the sample. Meaningless unless <see cref="HasVerdict"/>.</param>
/// <param name="Score">The deviation this detector measured, in <paramref name="ScoreKind"/> units.</param>
/// <param name="ScoreKind">The scale <paramref name="Score"/> is expressed on.</param>
/// <param name="Evidence">
/// Share of the detector's configured baseline that was actually populated, 0 to 1. This is how
/// much data is behind the score — <em>not</em> a probability that the score is correct. No
/// detector here can estimate the latter, so none of them claims to.
/// </param>
/// <param name="SampleCount">Samples this detector held for the channel when it answered.</param>
/// <param name="Reason">Why the verdict came out this way, including why one was withheld.</param>
public sealed record DetectorVerdict(
    string? DetectorId,
    bool IsAnomaly,
    double Score,
    DetectorScoreKind ScoreKind,
    double Evidence,
    int SampleCount,
    string Reason)
{
    /// <summary>True when this result carries an actual judgement.</summary>
    public bool HasVerdict => !string.IsNullOrEmpty(DetectorId);

    /// <summary>A verdict withheld, with the reason it was withheld.</summary>
    public static DetectorVerdict NotJudged(string reason, int sampleCount = 0, double evidence = 0.0) =>
        new(null, false, 0.0, DetectorScoreKind.None, evidence, sampleCount, reason);

    /// <summary>A verdict actually reached.</summary>
    public static DetectorVerdict Judged(
        string detectorId,
        bool isAnomaly,
        double score,
        DetectorScoreKind scoreKind,
        double evidence,
        int sampleCount,
        string reason) =>
        new(detectorId, isAnomaly, score, scoreKind, evidence, sampleCount, reason);

    /// <summary>One line naming the detector, its answer and its scale.</summary>
    public string Describe() => HasVerdict
        ? string.Create(CultureInfo.InvariantCulture,
            $"{DetectorId}: {(IsAnomaly ? "ANOMALY" : "normal")} {Score:0.###} {ScoreKind} ({Reason})")
        : $"(no verdict) {Reason}";
}
