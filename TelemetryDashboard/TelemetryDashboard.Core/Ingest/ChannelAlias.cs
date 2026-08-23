namespace TelemetryDashboard.Core.Ingest;

/// <summary>
/// What one name a device sends actually is, in this installation's terms.
/// </summary>
/// <param name="Channel">The channel id the profile declares, e.g. <c>psfb.output_voltage</c>.</param>
/// <param name="Unit">
/// The unit the value should be understood in once <see cref="Gain"/> has been applied, or empty to
/// keep whatever the device sent.
/// </param>
/// <param name="Gain">Multiplier applied to the raw value. 1 leaves it alone.</param>
/// <param name="Offset">Added after the multiplier. 0 leaves it alone.</param>
/// <remarks>
/// This exists because the firmware on a bench is not the firmware this product generates. A real
/// STM32 says <c>Vout</c> and reports millivolts; the profile declares <c>psfb.output_voltage</c>
/// in volts and states a band in volts. Without something to join them the readings arrive, chart
/// themselves under the device's name, and every band, computed channel and twin placement keyed on
/// the declared id quietly matches nothing — data on screen and no alarm behind it, which is the
/// most dangerous shape a monitoring tool can take.
/// <para>
/// The unit travels with the rename rather than being left to the device, because a limit is unit
/// checked: a band written in V against a channel still calling itself mV refuses to judge, and a
/// rule that cannot fire looks exactly like a machine that is behaving.
/// </para>
/// </remarks>
public sealed record ChannelAlias(string Channel, string Unit = "", double Gain = 1.0, double Offset = 0.0)
{
    /// <summary>Whether this alias changes the value at all.</summary>
    public bool Scales => Gain != 1.0 || Offset != 0.0;

    /// <summary>Applies the calibration to a raw reading.</summary>
    public double Apply(double raw) => raw * Gain + Offset;
}
