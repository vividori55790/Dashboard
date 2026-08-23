using System;
using System.Collections.Generic;
using System.Linq;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// Whether the bands a profile declares are judging anything at all.
/// </summary>
/// <remarks>
/// A band that has never been evaluated is silent, and so is a band on a healthy machine. Nothing
/// on a screen separates them: the readings arrive, they chart, and nothing compares them to
/// anything. That is what a channel name the device does not use looks like, it is what a rig
/// commissioned in stages looks like, and the two have different fixes.
/// <para>
/// Said once per band, and only after data has been flowing long enough for silence to mean
/// something. Reported at the moment a profile is applied it would name every band, because nothing
/// has arrived yet; reported on every sweep it would be noise an operator learns to skip, and a
/// line somebody has learned to skip is a line that is no longer there.
/// </para>
/// <para>
/// Separate from the panel that shows it because the decision is not a WPF one, and the control it
/// used to live in cannot be constructed outside a running application — which left the one thing
/// worth checking, that it speaks at all and only once, reachable only by running the shell and
/// watching an event log for thirty seconds.
/// </para>
/// </remarks>
public sealed class LimitArmingWatch
{
    /// <summary>
    /// How long a band may stay unevaluated before it is worth saying so.
    /// </summary>
    /// <remarks>
    /// Long enough that a slow channel is not accused of being missing, short enough that the
    /// answer arrives while the operator is still the person who just plugged the rig in.
    /// </remarks>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(30);

    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private DateTime _since = DateTime.UtcNow;
    private bool _summarySaid;

    public TimeSpan Grace { get; set; } = DefaultGrace;

    /// <summary>
    /// Forgets what was said, for a profile change.
    /// </summary>
    /// <remarks>
    /// The grace restarts with it. A new profile's bands have had no chance to see anything, and
    /// carrying the old clock across would accuse every one of them the moment it was chosen.
    /// </remarks>
    public void Reset(DateTime nowUtc)
    {
        _reported.Clear();
        _summarySaid = false;
        _since = nowUtc;
    }

    /// <summary>What is worth saying about <paramref name="rows"/> now, or nothing.</summary>
    public IReadOnlyList<string> Sweep(IReadOnlyList<LimitMonitor.RuleState>? rows, DateTime nowUtc)
    {
        if (rows is null || rows.Count == 0) return [];
        if (nowUtc - _since < Grace) return [];

        var lines = new List<string>();

        foreach (LimitMonitor.RuleState row in rows
                     .Where(r => r.Evaluated == 0 && r.UnitMismatch is null)
                     .Where(r => _reported.Add(r.Declaration)))
        {
            // The channel it wants is named, because that is the fix. Nine times in ten the device
            // is sending the same quantity under its own name and a wire rule is one line away.
            lines.Add(
                $"{row.Declaration} 은(는) 아직 한 번도 판정하지 않았습니다 — "
                + $"'{row.Channel}' 이름으로 도착한 샘플이 없습니다. "
                + "장비가 다른 이름을 쓰고 있다면 이름 매핑을 확인하세요.");
        }

        if (_summarySaid) return lines;

        // Said once either way, because "all seven are judging" is the answer somebody wants before
        // leaving a rig unattended, and a screen that only ever speaks up about faults cannot give
        // it.
        _summarySaid = true;
        int judging = rows.Count(r => r.Evaluated > 0);

        lines.Add(judging == rows.Count
            ? $"선언된 한계 {rows.Count}개가 모두 판정 중입니다."
            : $"선언된 한계 {rows.Count}개 중 {judging}개만 판정 중입니다. "
              + "판정하지 않는 한계는 조용한 것이 아니라 기계를 지키지 않고 있는 것입니다.");

        return lines;
    }
}
