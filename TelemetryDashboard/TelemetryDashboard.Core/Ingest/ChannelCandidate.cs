using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Core.Ingest;

/// <summary>One declared channel a reading could be, and how well it actually fits.</summary>
/// <param name="Gain">What the reading must be multiplied by to be in the declared unit.</param>
/// <param name="Score">Lower is a better fit. See <see cref="RuleDraft.Candidates"/>.</param>
/// <param name="Tightness">
/// The band's width measured in units of the reading itself. A band 280 A wide around a 3.2 A
/// reading scores 87 and is not evidence of anything; one 16 V wide around 48 V scores 0.33.
/// </param>
public sealed record ChannelCandidate(
    ProfileChannel Declared, double Gain, double Score, double Tightness)
{
    /// <summary>Loose enough that the reading being inside it says nothing.</summary>
    /// <remarks>
    /// This profile declares grid.voltage as 0..440 V, so every voltage on the bench falls inside
    /// it — including a 12 V auxiliary rail. Offering that as the answer because it is the only
    /// band containing 12 is how a drafting tool teaches people to distrust it.
    /// </remarks>
    public bool IsLoose => Tightness > 5.0;
}
