using System;
using System.Collections.Generic;
using System.Globalization;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.UI.Controls;

/// <summary>
/// Watching the safe bands the active profile declares.
/// </summary>
/// <remarks>
/// The shell judged every reading statistically and never once against an engineering limit. The
/// DAB/PSFB profile states seven of them — <c>psfb.output_voltage[V] in 45..51</c> and the rest —
/// the host enforces them, and the desktop application loaded the same profile, drew the same
/// channels, and compared nothing. An operator at the bench watching the rail leave its band saw
/// the number change colour only if the excursion also happened to look statistically unusual.
/// <para>
/// The two answer different questions and neither replaces the other. "Unusual lately" is a hint
/// about a distribution; "outside the band somebody agreed" is a fact about the machine, and it is
/// the one that has an action attached to it.
/// </para>
/// </remarks>
public partial class ControlPanelControl
{
    private LimitMonitor? _limits;
    private readonly HashSet<string> _unitWarned = new(StringComparer.Ordinal);

    /// <summary>
    /// Adopts <paramref name="profile"/>'s declared bands, and reports what could not be read.
    /// </summary>
    /// <returns>One line per declaration that was skipped, for the caller to log.</returns>
    /// <remarks>
    /// State is dropped with the old profile on purpose. A band carried over from another rig would
    /// announce a recovery for a limit that is no longer being watched, or worse, stay silent about
    /// a first excursion because the channel was already outside a band nobody declared any more.
    /// </remarks>
    public IReadOnlyList<string> ApplyLimits(MonitoringProfile? profile)
    {
        LimitDeclarations.Resolution resolved = LimitDeclarations.Resolve(profile?.Limits);

        _limits = resolved.Monitor;
        _unitWarned.Clear();
        return resolved.Warnings;
    }

    /// <summary>How many bands are being watched, for the caller to say so.</summary>
    public int WatchedLimitCount => _limits?.Rules.Count ?? 0;

    /// <summary>Compares one reading against every band that watches its channel.</summary>
    /// <remarks>
    /// Only the entry into a breach interrupts, matching what the anomaly path already does: a
    /// banner raised again on every sample while a rail sits below its floor is an alarm nobody
    /// reads. The return to inside is logged rather than announced, because "nothing is wrong any
    /// more" carries no action.
    /// </remarks>
    private void RaiseIfOutsideLimits(string node, string variable, double value, string? unit)
    {
        if (_limits is null) return;

        string channel = $"{node}.{variable}";

        foreach ((ChannelLimit rule, LimitTransition transition) in
                 _limits.Evaluate(channel, value, unit, DateTime.UtcNow))
        {
            switch (transition)
            {
                case LimitTransition.Entered:
                    string message = string.Create(CultureInfo.InvariantCulture,
                        $"{channel}: {rule.Explain(value)}");
                    LogMessage("ALARM", $"[LIMIT] {message}  ({rule.Declaration})");
                    AlertRaised?.Invoke(message, true);
                    break;

                case LimitTransition.Cleared:
                    LogMessage("DATA", $"[LIMIT] {channel}: back inside {rule.Declaration}");
                    break;

                case LimitTransition.UnitMismatch when _unitWarned.Add(channel + "|" + rule.Declaration):
                    // Once per rule and channel. A limit written in one unit against a channel
                    // reporting another can never fire, and a rule that never fires looks exactly
                    // like a machine that is behaving.
                    LogMessage("ERROR",
                        $"[LIMIT] {rule.Declaration} cannot judge {channel}: it is written in "
                        + $"{rule.Unit} and this channel reports "
                        + (string.IsNullOrWhiteSpace(unit) ? "no unit" : unit));
                    break;
            }
        }
    }

    /// <summary>Whether any watched band is currently breached, for the stream to carry.</summary>
    public bool AnyLimitBreached => _limits?.AnyBreached == true;
}
