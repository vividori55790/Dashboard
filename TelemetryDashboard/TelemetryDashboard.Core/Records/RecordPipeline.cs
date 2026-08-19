using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TelemetryDashboard.Core.Records;

/// <summary>What one stage did with the records it was offered.</summary>
public sealed record StageActivity(string Stage, long Accepted, long Declined, long Faulted)
{
    /// <summary>Records offered to the stage, whatever the outcome.</summary>
    public long Offered => Accepted + Declined + Faulted;
}

/// <summary>
/// Fans records out to the stages that can handle them, and counts the ones that could not.
/// </summary>
/// <remarks>
/// A declined record is normal, not an error: a text value reaching a z-score stage is exactly
/// what a mixed pipeline looks like. What would be wrong is losing track of it. The per-stage
/// counters mean "this analyser saw 4,000 records and scored none of them" is a visible fact
/// rather than something an operator has to deduce from an empty chart.
/// </remarks>
public sealed class RecordPipeline
{
    private readonly List<IRecordStage> _stages = new();
    private readonly Dictionary<string, long[]> _counters = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private const int Accepted = 0;
    private const int Declined = 1;
    private const int Faulted = 2;

    /// <summary>Adds a stage. Names must be unique so counters stay attributable.</summary>
    public RecordPipeline Register(IRecordStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);

        lock (_gate)
        {
            if (!_counters.TryAdd(stage.Name, new long[3]))
            {
                throw new ArgumentException($"A stage named '{stage.Name}' is already registered.", nameof(stage));
            }
            _stages.Add(stage);
        }

        return this;
    }

    public int StageCount
    {
        get { lock (_gate) return _stages.Count; }
    }

    /// <summary>
    /// Offers a record to every stage, returning how many accepted it.
    /// </summary>
    /// <remarks>
    /// A stage that throws is counted as faulted and the remaining stages still run. One failing
    /// sink must not stop a record reaching the others — losing a recording because an alert
    /// webhook timed out would be the tail wagging the dog.
    /// </remarks>
    public async ValueTask<int> DispatchAsync(DataRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        IRecordStage[] snapshot;
        lock (_gate) snapshot = _stages.ToArray();

        int accepted = 0;

        foreach (IRecordStage stage in snapshot)
        {
            if (!stage.CanHandle(record.Value))
            {
                Bump(stage.Name, Declined);
                continue;
            }

            try
            {
                await stage.ProcessAsync(record, cancellationToken).ConfigureAwait(false);
                Bump(stage.Name, Accepted);
                accepted++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                Bump(stage.Name, Faulted);
            }
        }

        return accepted;
    }

    /// <summary>Per-stage tallies, for the diagnostics surface.</summary>
    public IReadOnlyList<StageActivity> Activity()
    {
        lock (_gate)
        {
            return _stages
                .Select(s => new StageActivity(s.Name, _counters[s.Name][Accepted], _counters[s.Name][Declined], _counters[s.Name][Faulted]))
                .ToList();
        }
    }

    private void Bump(string stage, int slot)
    {
        lock (_gate)
        {
            if (_counters.TryGetValue(stage, out long[]? counts)) counts[slot]++;
        }
    }
}
