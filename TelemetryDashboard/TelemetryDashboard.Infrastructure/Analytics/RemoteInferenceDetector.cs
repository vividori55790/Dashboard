using System;
using System.Globalization;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Analytics.Detectors;
using TelemetryDashboard.Core.Resilience;

namespace TelemetryDashboard.Infrastructure.Analytics;

/// <summary>
/// A detector whose opinion comes from a model outside this process.
/// </summary>
/// <remarks>
/// <b>It never waits.</b> <see cref="Evaluate"/> appends the sample, may hand a completed window to
/// a bounded queue, and then reports on the newest answer that has already come back. The model is
/// therefore always describing a window that ended a moment ago, and the verdict says so — including
/// how long ago, so a reader can see whether the model is keeping up.
///
/// <b>It degrades to silence, never to a default.</b> There is no code path that produces a score
/// this detector did not receive. Endpoint down, endpoint slow, endpoint answering with something
/// unreadable, queue full, or an answer that has simply gone stale — every one of them yields
/// <see cref="DetectorVerdict.NotJudged"/> with a reason, and the counters in <see cref="Tally"/>
/// say which. That matters more than the happy path: a monitoring system whose model quietly
/// returns zero when it cannot reach anything is worse than one with no model at all, because it
/// looks like it is working.
/// </remarks>
public sealed partial class RemoteInferenceDetector : IChannelDetector, IAsyncDisposable
{
    private readonly BoundedChannelRegistry<InferenceChannelState> _states;
    private readonly InferenceDispatchQueue _queue;
    private readonly ChannelSelector _channels;
    private readonly InferenceSpec _spec;
    private readonly Func<DateTime> _clock;

    /// <param name="endpoint">The model. Consulted only from the dispatch pump, never inline.</param>
    /// <param name="spec">Window, threshold, timeout and the staleness limit.</param>
    /// <param name="label">The operator's name for this entry, prefixed onto every verdict.</param>
    /// <param name="clock">Wall clock, injectable so staleness can be tested without waiting.</param>
    public RemoteInferenceDetector(
        IInferenceEndpoint endpoint,
        InferenceSpec spec,
        string? label = null,
        Func<DateTime>? clock = null,
        int maxChannels = 10_000)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Window < 2) throw new ArgumentOutOfRangeException(nameof(spec), "A model window must hold at least two samples.");

        _spec = spec;
        _clock = clock ?? (() => DateTime.UtcNow);
        _channels = new ChannelSelector(spec.Channels);
        _states = new BoundedChannelRegistry<InferenceChannelState>(maxChannels);

        // One tally shared with the endpoint. Two would have to agree, and the first time they did
        // not the operator would be left choosing between them.
        Tally = endpoint.Tally;
        _queue = new InferenceDispatchQueue(endpoint, spec.QueueCapacity, Tally, OnScored);

        string core = $"inference/{endpoint.EndpointId}/w{spec.Window}"
            + $"/t{spec.Threshold.ToString("0.###", CultureInfo.InvariantCulture)}";
        DetectorId = string.IsNullOrWhiteSpace(label) ? core : label!.Trim() + ":" + core;
    }

    /// <inheritdoc />
    public string DetectorId { get; }

    /// <summary>What the model delivered, and every way it failed to. Shared with the endpoint.</summary>
    public InferenceTally Tally { get; }

    /// <inheritdoc />
    public bool CanHandle(string channelName) => _channels.Matches(channelName);

    /// <inheritdoc />
    public void Reset(string channelName) => _states.Remove(channelName ?? string.Empty);

    /// <inheritdoc />
    public DetectorVerdict Evaluate(string channelName, double value, DateTime observedUtc)
    {
        if (!double.IsFinite(value))
        {
            return DetectorVerdict.NotJudged("sample is not a finite number; a dropped reading is not an excursion");
        }

        string channel = channelName ?? string.Empty;
        InferenceChannelState state = _states.GetOrAdd(channel, _ => new InferenceChannelState(_spec.Window), out _);

        lock (state)
        {
            state.Window.Add(value);

            if (state.Window.Count < _spec.Window)
            {
                return DetectorVerdict.NotJudged(
                    $"collecting the model's window: {state.Window.Count} of {_spec.Window} samples",
                    state.Window.Count, state.Window.Fill);
            }

            Dispatch(channel, state, observedUtc);
            return Quote(state);
        }
    }

    /// <summary>Hands a completed window to the queue, if one is due and none is outstanding.</summary>
    private void Dispatch(string channel, InferenceChannelState state, DateTime observedUtc)
    {
        state.SamplesSinceRequest++;

        if (state.InFlight) return;
        if (state.EverRequested && state.SamplesSinceRequest < _spec.SamplesBetweenRequests) return;

        var samples = new double[state.Window.Count];
        for (int i = 0; i < samples.Length; i++) samples[i] = state.Window[i];

        state.InFlight = true;
        state.EverRequested = true;
        state.SamplesSinceRequest = 0;

        // A refused offer means nothing will come back for this window, so the in-flight flag has
        // to be released here or the channel would wait forever for an answer nobody is producing.
        if (!_queue.Offer(new InferenceRequest(channel, samples, observedUtc, _spec.ModelId)))
        {
            state.InFlight = false;
        }
    }

    public ValueTask DisposeAsync() => _queue.DisposeAsync();
}
