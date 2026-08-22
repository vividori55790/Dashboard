using System;
using System.Collections.Generic;

namespace TelemetryDashboard.UI.ViewModels;

/// <summary>
/// Coordinates the non-visual side of alerting: spoken announcements, the silent toast fallback,
/// and per-channel threshold evaluation.
/// </summary>
/// <remarks>
/// Speech is modelled as state here and never invoked. A synthesiser is a process-wide,
/// machine-dependent resource: it blocks the calling thread, it is absent on server SKUs, and it
/// cannot be exercised in an automated run. The dialog layer observes this state and does the
/// speaking, which keeps the throttling and sanitising rules testable on their own.
/// </remarks>
public sealed class AlertUXService
{
    /// <summary>
    /// Announcements allowed to queue before the oldest is dropped.
    /// </summary>
    /// <remarks>
    /// A cascade fault raises hundreds of alerts a second while speech plays back at roughly one
    /// sentence every two seconds. Without a ceiling the queue outlives the incident and the
    /// operator hears a recital of history while the plant is still moving.
    /// </remarks>
    private const int MaxPendingVoiceAlerts = 5;

    private readonly Queue<string> _voiceQueue = new();
    private readonly Dictionary<string, (double Min, double Max)> _thresholds =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True while spoken alerts are permitted.</summary>
    public bool IsSpeechEnabled { get; private set; } = true;

    /// <summary>Announcements waiting to be spoken.</summary>
    public int PendingVoiceCount => _voiceQueue.Count;

    /// <summary>Text of the most recent toast, or empty when none has been raised.</summary>
    public string LastToastMessage { get; private set; } = string.Empty;

    /// <summary>Severity of the most recent alert.</summary>
    public bool LastAlertWasCritical { get; private set; }

    /// <summary>
    /// Queues a spoken announcement, returning false when nothing will be said.
    /// </summary>
    /// <remarks>
    /// The return value reports whether the operator will actually hear something, so an empty
    /// message or a muted station is a false rather than a silent success. A caller that treats
    /// queueing as delivery would otherwise suppress its own visual fallback.
    /// </remarks>
    public bool TriggerVoiceAlert(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || !IsSpeechEnabled) return false;

        // Trimmed from the front: during a cascade the newest state is the actionable one, so a
        // full queue drops its oldest entry rather than refusing the alert that just arrived.
        if (_voiceQueue.Count >= MaxPendingVoiceAlerts) _voiceQueue.Dequeue();

        _voiceQueue.Enqueue(SanitizeSpeechText(message));
        return true;
    }

    /// <summary>
    /// Takes the next announcement to be spoken, or null when there is nothing waiting.
    /// </summary>
    /// <remarks>
    /// The queue had no way out. This class documents that the layer above does the speaking and
    /// that it observes the state here — and it exposed only a count, so the text could be queued,
    /// throttled and sanitised, and never read by anything. A queue with no drain is a queue that
    /// fills to its ceiling and stays there.
    /// <para>
    /// Oldest first: within the ceiling the order alerts arrived in is the order they make sense
    /// in, and the ceiling has already dropped whatever was too old to matter.
    /// </para>
    /// </remarks>
    public string? TakeNextVoiceAlert() => _voiceQueue.Count > 0 ? _voiceQueue.Dequeue() : null;

    /// <summary>
    /// Turns spoken alerts off and discards anything already queued.
    /// </summary>
    /// <remarks>
    /// Called when the synthesiser is unavailable or the station is muted. The backlog is dropped
    /// rather than held: if speech ever returns, replaying alerts from an incident that has since
    /// been resolved is worse than the silence.
    /// </remarks>
    public void DisableSapiTts()
    {
        IsSpeechEnabled = false;
        _voiceQueue.Clear();
    }

    /// <summary>
    /// Raises an alert across every available channel and reports whether the operator was
    /// notified at all.
    /// </summary>
    /// <remarks>
    /// True even with speech unavailable, because the visual toast still fires. Alerting has to
    /// degrade rather than fail — a muted station is still a manned one — so only an empty message,
    /// which would notify nobody of nothing, returns false. Speech is reserved for critical alerts
    /// so the channel keeps its meaning.
    /// </remarks>
    public bool TriggerAlert(string message, bool isCritical)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;

        LastToastMessage = message;
        LastAlertWasCritical = isCritical;
        if (isCritical) TriggerVoiceAlert(message);

        return true;
    }

    /// <summary>
    /// Removes characters a speech engine would read as markup.
    /// </summary>
    /// <remarks>
    /// A synthesiser parses its prompt as XML when markup is enabled, so an unescaped angle bracket
    /// in a channel name swallows the rest of the sentence: the alert plays, sounds perfectly
    /// normal, and omits the value. Removing the three XML metacharacters is safer than escaping
    /// them, since none of the three carries any meaning when spoken aloud.
    /// </remarks>
    public string SanitizeSpeechText(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        return raw.Replace("<", string.Empty)
                  .Replace(">", string.Empty)
                  .Replace("&", string.Empty);
    }

    /// <summary>
    /// Sets the alarm band for a channel; readings outside it breach.
    /// </summary>
    /// <remarks>
    /// Bounds supplied in the wrong order are reordered rather than rejected. An inverted band
    /// breaches on every single sample, which trains the operator to ignore the channel — a far
    /// more expensive failure than a silently corrected typo.
    /// </remarks>
    public void SetThresholds(string channel, double min, double max)
    {
        if (string.IsNullOrWhiteSpace(channel)) return;

        _thresholds[channel] = (Math.Min(min, max), Math.Max(min, max));
    }

    /// <summary>
    /// True when a reading falls outside the channel's alarm band.
    /// </summary>
    /// <remarks>
    /// A channel with no configured band never breaches. Inventing limits for it would raise alarms
    /// nobody has agreed the meaning of, and the first response would be to disable the alert.
    /// Non-finite readings are decode faults rather than process excursions and are surfaced by the
    /// parser layer, so they are not reported as breaches here.
    /// </remarks>
    public bool EvaluateThreshold(string channel, double value)
    {
        if (string.IsNullOrWhiteSpace(channel) || !double.IsFinite(value)) return false;
        if (!_thresholds.TryGetValue(channel, out (double Min, double Max) band)) return false;

        return value < band.Min || value > band.Max;
    }
}
