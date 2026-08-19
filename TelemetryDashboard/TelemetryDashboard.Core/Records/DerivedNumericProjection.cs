using System;
using System.Threading;
using System.Threading.Tasks;

namespace TelemetryDashboard.Core.Records;

/// <summary>
/// Turns records of one shape into derived numeric records, so a non-numeric domain can be
/// analysed by the machinery built for telemetry.
/// </summary>
/// <remarks>
/// This is what makes the generalisation pay for itself rather than merely permit itself.
///
/// A ward's appointment stream carries <see cref="DataValue.Instant"/> values, which no z-score
/// can score. But the questions worth asking about it — waiting time, no-show rate, density by
/// hour — <em>are</em> numeric. Projecting those out produces ordinary numeric records, and from
/// that point the same rolling-statistics engine that watches a power converter watches the clinic,
/// with the same compression, the same DVR and the same charts. No numeric stage learns anything
/// about hospitals; no hospital code learns anything about z-scores.
///
/// Everything emitted is stamped <see cref="DataRecord.DerivedFrom"/> with this projection's name,
/// because a computed figure that cannot say where it came from is indistinguishable from a
/// measurement — which is the confusion the whole provenance design exists to prevent.
/// </remarks>
public sealed class DerivedNumericProjection : IRecordStage
{
    private readonly DataValueKind _accepts;
    private readonly Func<DataRecord, double?> _measure;
    private readonly string _keySuffix;
    private readonly string _unit;
    private readonly Func<DataRecord, CancellationToken, ValueTask> _emit;

    /// <param name="name">Identifies the projection in provenance and in pipeline counters.</param>
    /// <param name="accepts">The value shape this projection reads.</param>
    /// <param name="measure">
    /// Computes the figure, or returns <c>null</c> when this record does not yield one. Null is a
    /// first-class answer: a cancelled appointment has no waiting time, and emitting 0 for it
    /// would drag every average it touches toward a number nothing observed.
    /// </param>
    /// <param name="keySuffix">Appended to the source key so the derived series is distinguishable.</param>
    /// <param name="emit">Receives each derived record — usually back into the pipeline.</param>
    public DerivedNumericProjection(
        string name,
        DataValueKind accepts,
        Func<DataRecord, double?> measure,
        string keySuffix,
        string unit,
        Func<DataRecord, CancellationToken, ValueTask> emit)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A projection must be named so its output can be attributed.", nameof(name));
        }
        if (string.IsNullOrWhiteSpace(keySuffix))
        {
            throw new ArgumentException(
                "A key suffix is required, or the derived series would overwrite its own source.", nameof(keySuffix));
        }

        Name = name;
        _accepts = accepts;
        _measure = measure ?? throw new ArgumentNullException(nameof(measure));
        _keySuffix = keySuffix;
        _unit = unit ?? string.Empty;
        _emit = emit ?? throw new ArgumentNullException(nameof(emit));
    }

    public string Name { get; }

    /// <summary>Records read so far.</summary>
    public long ReadCount { get; private set; }

    /// <summary>Records that yielded no figure.</summary>
    /// <remarks>
    /// Exposed rather than logged. A projection quietly measuring nothing looks exactly like a
    /// quiet channel, and the derived series is empty either way.
    /// </remarks>
    public long UnmeasurableCount { get; private set; }

    public bool CanHandle(DataValue value) => value.Kind == _accepts;

    public async ValueTask ProcessAsync(DataRecord record, CancellationToken cancellationToken = default)
    {
        ReadCount++;

        double? measured = _measure(record);
        if (measured is not { } figure || !double.IsFinite(figure))
        {
            UnmeasurableCount++;
            return;
        }

        DataRecord derived = DataRecord.Derived(
            record.Key.Stream,
            record.Key.Key + _keySuffix,
            new DataValue.Numeric(figure, _unit),
            Name,
            record.Timestamp);

        await _emit(derived, cancellationToken).ConfigureAwait(false);
    }
}
