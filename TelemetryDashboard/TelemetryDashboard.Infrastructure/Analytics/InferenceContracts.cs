using System;
using System.Threading;
using System.Threading.Tasks;

namespace TelemetryDashboard.Infrastructure.Analytics;

/// <summary>One window of samples offered to a model for scoring.</summary>
/// <param name="Channel">Channel the window came from, so the answer can be routed back to it.</param>
/// <param name="Window">Oldest sample first. Length is whatever the model was trained on.</param>
/// <param name="WindowEndUtc">Observation time of the newest sample in the window.</param>
/// <param name="ModelId">Model the caller believes it is talking to.</param>
public sealed record InferenceRequest(string Channel, double[] Window, DateTime WindowEndUtc, string ModelId);

/// <summary>
/// A score a model actually returned.
/// </summary>
/// <remarks>
/// There is no "empty" or "default" instance of this type on purpose. Everywhere a model could have
/// answered and did not — a timeout, a refused connection, an unparseable body — the result is
/// <c>null</c>, and null is not a score. A struct with a zero default would have made "the endpoint
/// never replied" indistinguishable from "the endpoint replied 0.0", which for an anomaly score is
/// the difference between silence and an all-clear.
/// </remarks>
/// <param name="Score">The model's output, on whatever scale it was trained to emit.</param>
/// <param name="ModelJudgement">
/// The model's own verdict when it stated one, or null when it returned only a score and left the
/// threshold to the caller.
/// </param>
/// <param name="ModelId">Model identity as the endpoint reported it, or null when it reported none.</param>
/// <param name="WindowEndUtc">Observation time of the newest sample this score was computed from.</param>
/// <param name="ReceivedUtc">When the answer arrived, which is what its staleness is measured from.</param>
public sealed record InferenceScore(
    double Score,
    bool? ModelJudgement,
    string? ModelId,
    DateTime WindowEndUtc,
    DateTime ReceivedUtc);

/// <summary>
/// Something that can score a window of telemetry: a service over HTTP, a model file loaded in
/// this process, or a stub in a test.
/// </summary>
/// <remarks>
/// One interface for both shapes the project set out to support. The remote case is implemented by
/// <see cref="HttpInferenceEndpoint"/>. The in-process case — an ONNX session over a
/// <c>.onnx</c> file — is the same contract with a different body, which is why this seam is where
/// it is: adding it later changes what is constructed at startup and nothing else.
///
/// <para><b>Returning null is a first-class answer.</b> An implementation that cannot reach its
/// model, is not answered in time, or is answered with something it cannot read, returns null. It
/// must never substitute a neutral score, because a neutral score is a judgement of normality that
/// nothing measured.</para>
///
/// <para>Implementations are called from a background pump, never from the ingest thread, so they
/// may block on I/O — but they must still honour both their own timeout and the token.</para>
/// </remarks>
public interface IInferenceEndpoint
{
    /// <summary>Identifies the model and where it lives, for the detector id and the startup report.</summary>
    string EndpointId { get; }

    /// <summary>
    /// Where this endpoint records what it delivered and how it failed.
    /// </summary>
    /// <remarks>
    /// Part of the contract rather than an implementation detail, and shared with the detector in
    /// front of it, so a run has one set of numbers rather than two that have to be reconciled.
    /// Only the endpoint can tell a timeout from a refusal from an unreadable body; only the
    /// detector knows what it offered and what it had to leave unjudged. Both belong in one place
    /// or an operator gets half the story from each.
    /// </remarks>
    InferenceTally Tally { get; }

    /// <summary>Scores one window, or returns null when no usable answer came back.</summary>
    Task<InferenceScore?> ScoreAsync(InferenceRequest request, CancellationToken cancellationToken);
}
