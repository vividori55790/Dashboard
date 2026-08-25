using System.Text;
using TelemetryDashboard.Core.Security;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Tests;

/// <summary>
/// What a wrong password costs the host, now that a stranger can send one.
/// </summary>
/// <remarks>
/// The gate was built for a console reachable only from its own machine, and it charged every
/// failure a full PBKDF2 derivation on purpose: with one credential and no accounts, that is the
/// rate limit an endpoint gets for free. <c>--listen network</c> changed who can send a header,
/// and the reasoning only survives half the change. It is still right for a guess nobody has made
/// before. It is wrong for a repeated one — a script holding a stale password, a browser
/// re-sending what was typed once — where the same wrong string buys a tenth of a second of CPU
/// per attempt from the process that is reading the plant. Nobody has to be hostile for that.
/// <para>
/// Asserted on a counter rather than a stopwatch. A timing test would pass or fail on how loaded
/// the machine is, and the claim here is exact: the derivation ran, or it did not.
/// </para>
/// </remarks>
public class ConsoleAccessGateCostTests
{
    private const string Secret = "plant-floor-42";

    private static string Basic(string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("ignored:" + password));

    private static ConsoleAccessGate Gate(int cap = ConsoleAccessGate.DefaultMaxRememberedRefusals) =>
        new(PasswordCredential.Create(Secret), cap);

    [Fact]
    [Trait("Category", "Tier1")]
    public void RepeatingOneWrongPasswordDerivesOnce()
    {
        ConsoleAccessGate gate = Gate();

        for (int i = 0; i < 20; i++)
        {
            gate.Allows(Basic("the-old-password")).Should().BeFalse(
                "caching a refusal must not turn into accepting it on the second try");
        }

        gate.Derivations.Should().Be(1,
            "twenty attempts at one wrong string is a retry loop, and charging it twenty times a "
            + "tenth of a second is two seconds taken from ingest by a client that is merely stale");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void EveryNewGuessStillPaysInFull()
    {
        // The half that must not be optimised away. Brute force is exactly where the cost belongs,
        // and it is the case that varies the guess -- so the cache above never sees a hit.
        ConsoleAccessGate gate = Gate();

        for (int i = 0; i < 4; i++)
        {
            gate.Allows(Basic($"guess-{i}")).Should().BeFalse();
        }

        gate.Derivations.Should().Be(4,
            "a distinct guess is the case this endpoint has to stay slow for");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheRightPasswordStillWorksAfterBeingWrongAboutOthers()
    {
        ConsoleAccessGate gate = Gate();

        gate.Allows(Basic("wrong")).Should().BeFalse();
        gate.Allows(Basic(Secret)).Should().BeTrue();
        gate.Allows(Basic(Secret)).Should().BeTrue();

        gate.Derivations.Should().Be(2,
            "one for the refusal and one for the success; the second success is served from the "
            + "proven set, which is what makes a polling console affordable at all");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void PastTheCapTheEndpointGoesBackToChargingForEverything()
    {
        // The cap exists so that somebody varying their guess cannot grow this set without bound.
        // What it costs is the protection above, for as long as the process runs -- so the
        // behaviour past the cap is stated here rather than left to be discovered on a host that
        // has been up for a month. Two, not 1024, because the default would cost 1024 derivations
        // and about two minutes to reach.
        ConsoleAccessGate gate = Gate(cap: 2);

        gate.Allows(Basic("a"));
        gate.Allows(Basic("b"));
        long afterFilling = gate.Derivations;

        gate.Allows(Basic("a")).Should().BeFalse();
        gate.Derivations.Should().Be(afterFilling, "'a' was remembered before the set filled");

        gate.Allows(Basic("c")).Should().BeFalse();
        gate.Allows(Basic("c")).Should().BeFalse();
        gate.Derivations.Should().Be(afterFilling + 2,
            "'c' arrived after the cap, so it is not remembered and pays every time -- which is "
            + "the endpoint's original behaviour, not a new failure");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AMalformedHeaderCostsNothing()
    {
        // The cheapest thing a stranger can send. It must not reach the derivation at all, or the
        // rate limit is bypassed by sending rubbish instead of guesses.
        ConsoleAccessGate gate = Gate();

        gate.Allows(null).Should().BeFalse();
        gate.Allows("").Should().BeFalse();
        gate.Allows("Bearer something").Should().BeFalse();
        gate.Allows("Basic not-base64!!").Should().BeFalse();
        gate.Allows("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("no-colon"))).Should().BeFalse();

        gate.Derivations.Should().Be(0);
    }
}
