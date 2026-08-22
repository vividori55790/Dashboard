namespace TelemetryDashboard.UI.Controls;

/// <summary>
/// How many channels the scope draws at once, and how many it will hold at all.
/// </summary>
/// <remarks>
/// These were one number, and conflating them was the defect. A single cap of sixteen refused to
/// <em>create</em> the seventeenth channel a rig reported, so it never reached the toggle list
/// either — the operator could not tick it, untick something else, or even learn it existed. The
/// panel whose job is to show channels was hiding them.
/// <para>
/// They are different questions. How many traces can be drawn before the plot is unreadable and the
/// render loop expensive is a display budget, and the operator should be able to spend it however
/// they like. How many channels are worth holding at all is a guard against a source inventing a
/// fresh name per packet — a malformed parse, a device putting a serial number in the variable
/// field — and that ceiling belongs far above any real rig.
/// </para>
/// <para>
/// Derived channels made the old cap bite: a ten-channel rig with <c>--watch-intervals</c> and
/// <c>--watch-drift</c> reports thirty. Measured on the running application against a twenty-channel
/// profile, before and after: "Channels: 16 ... dropped 544 (544 past channel cap)" became
/// "Channels: 20 (4 unticked)" with nothing dropped, and ticking one of the four drew it.
/// </para>
/// </remarks>
public static class ScopeChannelBudget
{
    /// <summary>Channels drawn without the operator asking.</summary>
    public const int DefaultPlotted = 16;

    /// <summary>Channels held at all, however many a source invents.</summary>
    public const int Ceiling = 128;

    /// <summary>Whether a newly discovered channel starts drawn.</summary>
    /// <remarks>
    /// The budget is spent in arrival order, which is arbitrary — but arbitrary and visible beats
    /// arbitrary and hidden, and every channel past it is one tick away.
    /// </remarks>
    public static bool StartsVisible(int existingChannels) => existingChannels < DefaultPlotted;

    /// <summary>Whether another channel can be held at all.</summary>
    public static bool HasRoom(int existingChannels) => existingChannels < Ceiling;
}
