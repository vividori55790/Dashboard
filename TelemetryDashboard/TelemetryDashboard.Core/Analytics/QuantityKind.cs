namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// What kind of physical quantity a channel carries — or that nobody established one.
/// </summary>
/// <remarks>
/// <b>Where this vocabulary came from, and what was rejected.</b> Four specifications were read
/// before anything here was written, because a telemetry hub that invents its own quantity names
/// is a hub nothing else can be wired to. Each answered a different part of the question.
/// <para>
/// <b>UCUM supplies the names.</b> Its unit tables carry a "kind of quantity" column —
/// <c>electric potential</c>, <c>electric current</c>, <c>power</c>, <c>temperature</c>,
/// <c>pressure</c>, <c>frequency</c>, <c>electric resistance</c> — and it is the only widely
/// implemented standard that ships both the kind names and the unit codes, so the two cannot
/// disagree. One caveat is worth recording rather than discovering later: UCUM says only the
/// <c>c/s</c>, <c>c/i</c>, <c>M</c>, <c>value</c> and <c>definition</c> columns are normative, so
/// that column is a convention and not a contract. This enum is a subset of it, not a claim to
/// implement UCUM.
/// </para>
/// <para>
/// <b>UCUM also supplies the unit spelling</b> used by <see cref="ChannelClassification.Unit"/>:
/// <c>Cel</c> rather than <c>°C</c>, <c>Ohm</c>, <c>By</c> for byte, and — the one that earns its
/// keep — <c>[g]</c> in brackets for standard gravity as against <c>g</c> the gram. UCUM brackets
/// customary units precisely so those two cannot collide, and a device that writes a bare
/// <c>g</c> for vibration has thrown that distinction away. See <see cref="UnitVocabulary"/>.
/// </para>
/// <para>
/// <b>Prometheus and OpenMetrics supply the base-unit preference</b> wherever UCUM permits a
/// choice: celsius over kelvin, seconds, bytes over bits, grams over kilograms, and ratios carried
/// as 0–1 rather than as percent. They also supply the namespace/subsystem naming convention that
/// <see cref="SubsystemName"/> reads. What was <em>not</em> taken is their unit-as-name-suffix rule
/// (<c>_seconds</c>, <c>_bytes</c>, <c>_ratio</c>): it is only true of names authored under that
/// convention, and a hub cannot know whether a given device's names were.
/// </para>
/// <para>
/// <b>Sparkplug B is the closest industrial prior art, and its answer is why there is no unit enum
/// here.</b> The specification defines exactly one well-known metric property — <c>Quality</c>
/// (0 BAD, 192 GOOD, 500 STALE) — and its <c>MetaData</c> fields are about file transfer
/// (<c>content_type</c>, <c>size</c>, <c>md5</c>), not about physics. The <c>engUnit</c>,
/// <c>engLow</c> and <c>engHigh</c> keys everyone associates with Sparkplug are an
/// Ignition/Cirrus-Link convention carried in the free-text <c>PropertySet</c>, not spec-defined.
/// So Sparkplug answers <em>where a unit travels</em> — as free text beside the value, never as a
/// closed enum on the wire — and says nothing at all about what kind of quantity a metric is. That
/// gap is exactly what this file fills, and it is why a declared unit here is a string.
/// </para>
/// <para>
/// <b>OPC-UA <c>EUInformation</c> was read and rejected as the vocabulary.</b> Its <c>unitId</c> is
/// a UNECE Recommendation 20 common code packed into an int32; Rec 20 is a list of roughly two
/// thousand unit codes with no quantity-kind grouping a program can branch on, and a numeric id
/// makes a wrong classification invisible in a log. What <em>was</em> taken is its
/// <c>namespaceUri</c> idea — a unit code means nothing without naming the system it belongs to,
/// which is why the unit this type publishes is stated to be UCUM rather than left as bare text.
/// </para>
/// <para>
/// <b>QUDT was rejected as the source and supplies the concept name.</b> It has an OWL class
/// literally called <c>QuantityKind</c>, which is the right idea; it is also an RDF ontology with
/// no C# story, and importing a dimensional-analysis ontology to decide whether
/// <c>dab.bus_voltage</c> is a voltage is a large dependency for a small question.
/// </para>
/// </remarks>
public enum QuantityKind
{
    /// <summary>
    /// Nothing established a kind. The default, and the answer this whole taxonomy exists to be
    /// able to give: a channel labelled a temperature because its name contains a <c>t</c> and its
    /// values sit near 20 would go on to pick an axis and an alarm band, which makes a confident
    /// wrong label worse than none.
    /// </summary>
    Unclassified = 0,

    /// <summary>UCUM "electric potential". Volts.</summary>
    ElectricPotential,

    /// <summary>UCUM "electric current". Amperes.</summary>
    ElectricCurrent,

    /// <summary>UCUM "electric resistance". Ohms.</summary>
    ElectricResistance,

    /// <summary>UCUM "power". Watts.</summary>
    Power,

    /// <summary>UCUM "energy". Joules, and the watt-hour a plant actually reports.</summary>
    Energy,

    /// <summary>UCUM "temperature". Celsius by the Prometheus preference, kelvin accepted.</summary>
    Temperature,

    /// <summary>UCUM "pressure". Pascals, and the bar a plant actually reports.</summary>
    Pressure,

    /// <summary>UCUM "frequency". Hertz.</summary>
    Frequency,

    /// <summary>
    /// Revolutions per unit time. Dimensionally <see cref="Frequency"/> and semantically not: a
    /// shaft at 1500 rpm and a switching leg at 1500 Hz share a dimension and share no axis, no
    /// scale and no alarm band.
    /// </summary>
    RotationalFrequency,

    /// <summary>UCUM "acceleration". Where vibration in <c>g</c> lands.</summary>
    Acceleration,

    /// <summary>UCUM "length".</summary>
    Length,

    /// <summary>UCUM "mass". Grams, per the Prometheus preference over kilograms.</summary>
    Mass,

    /// <summary>UCUM "time". Seconds — durations, latencies, uptimes.</summary>
    Time,

    /// <summary>Bytes. Not a UCUM property; taken from the Prometheus base-unit list.</summary>
    DataSize,

    /// <summary>
    /// A count per unit time.
    /// </summary>
    /// <remarks>
    /// The weakest member here and it is worth saying why. UCUM has no "rate" property, and
    /// Prometheus does not model one either — it makes the channel a counter, suffixes it
    /// <c>_total</c>, and derives the rate at query time. So a rate is really a consequence of a
    /// channel being a counter rather than a gauge, which is a second axis this taxonomy does not
    /// have. Consequently nothing can reach this kind from a declared unit: <c>/s</c> is
    /// dimensionally <c>Hz</c> and no unit distinguishes the two. It is reachable only from a name,
    /// and so only ever as a proposal. Adding a counter/gauge axis is the better answer and is a
    /// product decision, recorded rather than taken.
    /// </remarks>
    Rate,

    /// <summary>
    /// A dimensionless fraction — duty cycle, efficiency, utilisation. Prometheus carries these as
    /// 0–1 and names them <c>_ratio</c>; a declared <c>%</c> says the same kind on a 0–100 scale.
    /// </summary>
    Ratio,

    /// <summary>UCUM unity, <c>1</c>. A count or an index that is a number and not a measurement.</summary>
    Dimensionless
}
