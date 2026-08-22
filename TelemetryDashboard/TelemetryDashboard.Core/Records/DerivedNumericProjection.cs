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
    private readonly Func<DataRecord, string> _unitOf;
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
        Func<DataRecord, CancellationToken, ValueTask> emit,
        Func<DataRecord, string>? unitOf = null)
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

        // A fixed unit is right for a projection that always measures the same quantity -- an
        // interval is seconds whatever it was computed from. It is wrong for one whose output
        // carries its input's unit: drift on a voltage is volts, on a temperature is degrees, and
        // publishing it unitless leaves an operator writing a limit against a number whose scale
        // they have to guess. Surfaced by the first projection of the second kind.
        _unitOf = unitOf ?? (_ => unit ?? string.Empty);
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

    /// <summary>Records this projection had already produced and was offered again.</summary>
    /// <remarks>
    /// Counted rather than passed over quietly. A non-zero value here is the pipeline feeding a
    /// projection its own output, which is correct and expected once the emit target is the
    /// pipeline itself — but it is also exactly the shape of a configuration that would have
    /// recursed, so it is worth being able to see.
    /// </remarks>
    public long SelfDeclinedCount { get; private set; }

    public bool CanHandle(DataValue value) => value.Kind == _accepts;

    /// <summary>
    /// Measures one record and emits the derived one, unless this projection made it.
    /// </summary>
    /// <remarks>
    /// The self-check is not defensive tidiness; without it this class cannot be used at all in the
    /// arrangement it was written for. <see cref="_emit"/> is documented as feeding the pipeline,
    /// and the pipeline offers every record to every stage whose <see cref="CanHandle"/> matches
    /// the value <em>shape</em> — which a derived numeric record does, being numeric. So the first
    /// record produces a derivative, the derivative is offered straight back, and the keys grow a
    /// suffix per turn until the stack ends the process.
    /// <para>
    /// Nothing had ever noticed, because nothing had ever registered one of these in a live
    /// pipeline. The guard is on this projection's own name rather than on
    /// <see cref="DataRecord.IsDerived"/>, so one projection reading another's output — a rate
    /// computed from a waiting time — still works.
    /// </para>
    /// </remarks>
    public async ValueTask ProcessAsync(DataRecord record, CancellationToken cancellationToken = default)
    {
        if (string.Equals(record.DerivedFrom, Name, StringComparison.Ordinal))
        {
            SelfDeclinedCount++;
            return;
        }

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
            new DataValue.Numeric(figure, _unitOf(record)),
            Name,
            record.Timestamp,
            record.Source);

        await _emit(derived, cancellationToken).ConfigureAwait(false);
    }
}
