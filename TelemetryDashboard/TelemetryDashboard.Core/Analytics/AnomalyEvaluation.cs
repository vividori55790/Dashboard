namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// Outcome of one <see cref="AnomalyEngine"/> pass over a batch of samples.
/// </summary>
/// <remarks>
/// The reason travels with the verdict rather than going to a log line. "Nothing is wrong" and
/// "I could not tell" are indistinguishable to a caller that reads only the boolean, and an
/// operator who cannot tell them apart will read an unmonitored channel as a healthy one.
/// <para>
/// A separate type from <c>AnomalyResult</c>, which reports a single live sample against a rolling
/// channel history. This one reports a batch verdict and has to explain itself.
/// </para>
/// </remarks>
/// <param name="IsAnomaly">True when the newest usable sample exceeds the engine's sigma threshold.</param>
/// <param name="Reason">Why the verdict came out this way, including why one was withheld.</param>
/// <param name="ZScore">
/// Deviation of the newest sample in standard deviations. Zero when the series carries no
/// measurable variance, since a channel that never moved says nothing about what an excursion
/// would look like.
/// </param>
/// <param name="ProcessedSampleCount">
/// Samples that survived filtering. Non-finite readings are excluded, so this is usually smaller
/// than the input length and is the number the verdict actually rests on.
/// </param>
public sealed record AnomalyEvaluation(
    bool IsAnomaly,
    string Reason,
    double ZScore,
    int ProcessedSampleCount);
