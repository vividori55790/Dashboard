using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Analytics;

/// <summary>
/// The words a channel name may contain that mean something, and what each proposes.
/// </summary>
/// <remarks>
/// Split from the tokeniser because it is the half that grows and the half an operator would want
/// to read. Nothing in it is decisive: every entry here produces a proposal at
/// <see cref="ClassificationConfidence.Low"/> and stays there unless a declared unit agrees.
/// <para>
/// Words that were considered and left out, because each is genuinely two things: <c>speed</c>
/// (linear or rotational), <c>load</c> (a ratio or the watts a load draws), <c>size</c> (bytes or a
/// dimension), <c>total</c> (a Prometheus counter suffix, not a quantity), <c>level</c>, and
/// <c>impedance</c> (complex, and not the resistance kind). <c>temp</c> is in, and it is the
/// weakest entry here — it is temperature on every rig and "temporary" in some software — which is
/// exactly why a name hint alone never rises above <see cref="ClassificationConfidence.Low"/>.
/// </para>
/// </remarks>
public static partial class ChannelNameHints
{
    private static readonly Dictionary<string, QuantityKind> Words = new(StringComparer.Ordinal)
    {
        ["voltage"] = QuantityKind.ElectricPotential,
        ["volt"] = QuantityKind.ElectricPotential,
        ["volts"] = QuantityKind.ElectricPotential,

        ["current"] = QuantityKind.ElectricCurrent,
        ["amperage"] = QuantityKind.ElectricCurrent,

        ["resistance"] = QuantityKind.ElectricResistance,

        ["power"] = QuantityKind.Power,
        ["wattage"] = QuantityKind.Power,

        ["energy"] = QuantityKind.Energy,

        ["temperature"] = QuantityKind.Temperature,
        ["temp"] = QuantityKind.Temperature,

        ["pressure"] = QuantityKind.Pressure,

        ["frequency"] = QuantityKind.Frequency,
        ["freq"] = QuantityKind.Frequency,

        ["rpm"] = QuantityKind.RotationalFrequency,

        ["vibration"] = QuantityKind.Acceleration,
        ["acceleration"] = QuantityKind.Acceleration,
        ["accel"] = QuantityKind.Acceleration,

        ["length"] = QuantityKind.Length,
        ["distance"] = QuantityKind.Length,
        ["displacement"] = QuantityKind.Length,

        ["mass"] = QuantityKind.Mass,
        ["weight"] = QuantityKind.Mass,

        ["duration"] = QuantityKind.Time,
        ["latency"] = QuantityKind.Time,
        ["elapsed"] = QuantityKind.Time,
        ["uptime"] = QuantityKind.Time,

        ["bytes"] = QuantityKind.DataSize,

        ["rate"] = QuantityKind.Rate,
        ["throughput"] = QuantityKind.Rate,

        ["ratio"] = QuantityKind.Ratio,
        ["duty"] = QuantityKind.Ratio,
        ["efficiency"] = QuantityKind.Ratio,
        ["utilization"] = QuantityKind.Ratio,
        ["utilisation"] = QuantityKind.Ratio,
        ["fraction"] = QuantityKind.Ratio,

        ["count"] = QuantityKind.Dimensionless,
        ["index"] = QuantityKind.Dimensionless
    };
}
