using System;

namespace TelemetryDashboard.Core.Records;

/// <summary>Discriminator for <see cref="DataValue"/>, for dispatch and serialisation.</summary>
public enum DataValueKind
{
    Numeric,
    Text,
    Instant,
    Flag,
    Blob
}

/// <summary>
/// A measurement, closed over the shapes this system can carry.
/// </summary>
/// <remarks>
/// This is the type that lets the hub manage things that are not telemetry — an appointment time,
/// a spreadsheet cell, a status string — without giving up what makes it good at telemetry.
///
/// The obvious generalisation is to widen the pipeline's <c>double</c> to <c>object</c>. That would
/// destroy the parts that work: Gorilla compression <em>is</em> XOR over IEEE-754 bit patterns and
/// stops being a compressor without a double, and a z-score over a string is not a weaker answer
/// but a meaningless one. So the union lives one layer <em>above</em> the numeric pipeline rather
/// than inside it. A <see cref="Numeric"/> value projects losslessly onto the existing path and
/// inherits all of it; the other shapes flow through routing, storage, streaming and plugins while
/// <em>declining</em> numeric analysis, which <see cref="IRecordStage.CanHandle"/> makes explicit
/// instead of leaving it to a cast that happens to succeed.
///
/// The hierarchy is closed: the private constructor means only the nested types can derive, so a
/// <c>switch</c> over the cases is exhaustive and adding a shape is a deliberate edit here.
/// </remarks>
public abstract record DataValue
{
    private DataValue() { }

    /// <summary>Which case this is.</summary>
    public abstract DataValueKind Kind { get; }

    /// <summary>A quantity that can be compressed, averaged, plotted and scored.</summary>
    /// <param name="Value">The magnitude. <see cref="double.NaN"/> means "no reading", never zero.</param>
    /// <param name="Unit">Physical unit, empty when dimensionless.</param>
    public sealed record Numeric(double Value, string Unit = "") : DataValue
    {
        public override DataValueKind Kind => DataValueKind.Numeric;

        /// <summary>False when the reading is absent or non-finite, and so unsafe to aggregate.</summary>
        public bool IsMeasured => double.IsFinite(Value);
    }

    /// <summary>Free or categorical text: a status, a name, a spreadsheet cell.</summary>
    public sealed record Text(string Value) : DataValue
    {
        public override DataValueKind Kind => DataValueKind.Text;
    }

    /// <summary>
    /// A point in time that is itself the measurement — an appointment, a deadline, an event stamp.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="DataRecord.Timestamp"/>, which says when the record was observed.
    /// A booking made on Monday for a Thursday slot carries Monday as the record timestamp and
    /// Thursday as the value; collapsing the two loses exactly the question worth asking.
    /// </remarks>
    public sealed record Instant(DateTimeOffset Value) : DataValue
    {
        public override DataValueKind Kind => DataValueKind.Instant;
    }

    /// <summary>A boolean state: attended or not, valve open or closed.</summary>
    public sealed record Flag(bool Value) : DataValue
    {
        public override DataValueKind Kind => DataValueKind.Flag;
    }

    /// <summary>Opaque bytes with a media type — an image, a document, a captured frame.</summary>
    public sealed record Blob(ReadOnlyMemory<byte> Value, string MediaType) : DataValue
    {
        public override DataValueKind Kind => DataValueKind.Blob;

        public int ByteCount => Value.Length;
    }
}
