using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Analytics.Detectors;

/// <summary>Where an external model lives, and therefore how it is reached.</summary>
public enum InferenceRuntime
{
    /// <summary>No external model.</summary>
    None,

    /// <summary>A model served over HTTP: a window of samples is POSTed and a score comes back.</summary>
    Http,

    /// <summary>
    /// A model file scored in this process, e.g. through ONNX Runtime.
    /// </summary>
    /// <remarks>
    /// Declared here because it is the shape the seam was designed around, and refused by the host
    /// unless a build actually carries a runtime that can load the file. Accepting the setting and
    /// then scoring nothing would be the exact failure this project is built to avoid: a
    /// configuration that looks applied, a model that never runs, and a dashboard that cannot tell
    /// the difference.
    /// </remarks>
    InProcess
}

/// <summary>
/// How an external model is consulted, and what happens when it does not answer.
/// </summary>
/// <remarks>
/// Every timing field here exists because of a failure mode rather than a preference.
/// <see cref="TimeoutMs"/> bounds one call; <see cref="QueueCapacity"/> bounds the backlog when the
/// endpoint is slower than the feed; <see cref="MaxScoreAgeMs"/> bounds how long a score that did
/// come back may still be quoted. That last one is the important one — without it, an endpoint that
/// answered once and then died would go on supplying a verdict for a sample it never saw.
/// </remarks>
public sealed class InferenceSpec
{
    /// <summary>Which shape of model this is.</summary>
    public InferenceRuntime Runtime { get; init; } = InferenceRuntime.None;

    /// <summary>URL to POST a window of samples to, for <see cref="InferenceRuntime.Http"/>.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Model file to load, for <see cref="InferenceRuntime.InProcess"/>.</summary>
    public string? ModelPath { get; init; }

    /// <summary>Model identity, carried into every verdict so a score can be traced to a version.</summary>
    public string ModelId { get; init; } = "model";

    /// <summary>Channels whose windows are sent to the model.</summary>
    public IReadOnlyList<string> Channels { get; init; } = Array.Empty<string>();

    /// <summary>Samples per request. Must match what the model was trained on.</summary>
    public int Window { get; init; } = 64;

    /// <summary>Score at or above which the model's output is treated as an anomaly.</summary>
    public double Threshold { get; init; } = 0.8;

    /// <summary>How long one request may take before it is abandoned and counted.</summary>
    public int TimeoutMs { get; init; } = 750;

    /// <summary>Windows that may be waiting to be sent before new ones are refused and counted.</summary>
    public int QueueCapacity { get; init; } = 32;

    /// <summary>
    /// How old a returned score may be and still be quoted as a verdict on the current sample.
    /// </summary>
    /// <remarks>
    /// Past this the detector reports no verdict again. A model that has stopped answering must
    /// stop producing judgements, not keep repeating its last one under a fresh timestamp.
    /// </remarks>
    public int MaxScoreAgeMs { get; init; } = 5_000;

    /// <summary>Samples between requests for one channel, so a fast feed cannot flood the model.</summary>
    public int SamplesBetweenRequests { get; init; } = 8;
}
