using TelemetryDashboard.Core.Analytics.Detectors;

namespace TelemetryDashboard.Infrastructure.Analytics;

/// <summary>
/// What <see cref="RemoteInferenceDetector"/> remembers about one channel between samples.
/// </summary>
/// <remarks>
/// Internal, and mutated only under a lock on the instance itself. Two threads touch it: the ingest
/// thread adding samples and reading the newest score, and the dispatch pump storing an answer that
/// arrived. That is the whole concurrency story, and keeping it to one lock per channel is why the
/// ingest path never contends with a model that is being slow somewhere else.
/// </remarks>
internal sealed class InferenceChannelState
{
    public InferenceChannelState(int windowSize) => Window = new DetectorWindow(windowSize);

    /// <summary>The rolling window of samples the model is scored on.</summary>
    public DetectorWindow Window { get; }

    /// <summary>Samples added since the last window was sent, so a fast feed cannot flood the model.</summary>
    public int SamplesSinceRequest;

    /// <summary>True once a window has been sent for this channel at all.</summary>
    /// <remarks>
    /// Separate from the counter so the first complete window is sent immediately rather than after
    /// another full throttle interval. Waiting would delay the model's first opinion by a whole
    /// interval on every channel, for no reason beyond an off-by-one in the throttle.
    /// </remarks>
    public bool EverRequested;

    /// <summary>True while a request for this channel is queued or in flight.</summary>
    /// <remarks>
    /// One outstanding request per channel. Without it a slow model accumulates one queued window
    /// per sample, and by the time an answer arrives it describes a window long past — a queue full
    /// of answers to stale questions is worse than no answers.
    /// </remarks>
    public bool InFlight;

    /// <summary>The newest usable score, or null when the model has never returned one.</summary>
    public InferenceScore? Latest;
}
