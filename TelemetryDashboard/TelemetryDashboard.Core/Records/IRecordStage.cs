using System.Threading;
using System.Threading.Tasks;

namespace TelemetryDashboard.Core.Records;

/// <summary>
/// One step on the universal record path: storage, streaming, analysis, an extension.
/// </summary>
/// <remarks>
/// <see cref="CanHandle"/> is the reason this interface exists. Without it a pipeline carrying
/// mixed values has only two options, and both are bad: cast and hope, which turns an appointment
/// time into whatever a numeric stage makes of it; or coerce everything to a common type, which is
/// how a missing reading becomes a confident 0.0. Making capability an explicit question means a
/// stage that cannot process a value says so, the pipeline records the refusal, and the operator
/// can see that four hundred records went past a stage untouched instead of inferring it from a
/// gap in a chart.
/// </remarks>
public interface IRecordStage
{
    /// <summary>Name used in diagnostics and in the pipeline's per-stage counters.</summary>
    string Name { get; }

    /// <summary>
    /// Whether this stage can do something meaningful with a value of this shape.
    /// </summary>
    /// <remarks>
    /// Answer for the <em>shape</em>, not the instance. "I handle numbers" is a capability; "I
    /// handle this particular number" is a decision that belongs in <see cref="ProcessAsync"/>.
    /// </remarks>
    bool CanHandle(DataValue value);

    /// <summary>Processes a record this stage has already accepted.</summary>
    ValueTask ProcessAsync(DataRecord record, CancellationToken cancellationToken = default);
}
