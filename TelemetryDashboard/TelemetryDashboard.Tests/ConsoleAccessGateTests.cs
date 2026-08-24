using System.Diagnostics;
using System.Text;
using TelemetryDashboard.Core.Security;
using TelemetryDashboard.Core.Streaming;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The lock, built before the door it is for.
/// </summary>
/// <remarks>
/// The console binds loopback only, and the argument for opening it to a network has always stalled
/// on one sentence: the endpoint has no authentication. The order matters. Adding the binding first
/// and the check afterwards would ship an operator a flag that reads like a lock and is not one,
/// which is worse than having no flag — they would bind wide believing they were covered.
/// </remarks>
public class ConsoleAccessGateTests
{
    private const string Secret = "plant-floor-42";

    private static ConsoleAccessGate Gate() => new(PasswordCredential.Create(Secret));

    private static string Basic(string user, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheRightPasswordIsAdmittedAndAWrongOneIsNot()
    {
        ConsoleAccessGate gate = Gate();

        gate.Allows(Basic("operator", Secret)).Should().BeTrue();
        gate.Allows(Basic("operator", "plant-floor-43")).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheUsernameIsIgnoredRatherThanQuietlyMeaningful()
    {
        // One credential, not accounts. If the name were checked, somebody would come to rely on it
        // and this would have become a user directory without anyone deciding to build one.
        ConsoleAccessGate gate = Gate();

        gate.Allows(Basic("anyone-at-all", Secret)).Should().BeTrue();
        gate.Allows(Basic("", Secret)).Should().BeTrue();
    }

    [Theory]
    [Trait("Category", "Tier2")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bearer some-token")]
    [InlineData("Basic")]
    [InlineData("Basic !!!not-base64!!!")]
    [InlineData("Basic bm8tY29sb24taGVyZQ==")]
    public void AnythingThatIsNotACredentialIsRefusedRatherThanThrowing(string? header)
    {
        // Everything reachable before authentication is reachable by anyone, so it has to survive
        // being sent rubbish on purpose. A gate that throws is a gate that fails whichever way the
        // caller of the day happens to handle exceptions.
        Gate().Allows(header).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void ARepeatedCorrectPasswordDoesNotPayForTheDerivationTwice()
    {
        // PBKDF2 at 210,000 iterations is about a tenth of a second and a console polls several
        // endpoints a second. Deriving on every request would make the product unusable and hand
        // anyone outside a denial of service for the price of one wrong header.
        ConsoleAccessGate gate = Gate();
        string header = Basic("operator", Secret);

        gate.Allows(header).Should().BeTrue();

        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++) gate.Allows(header).Should().BeTrue();
        clock.Stop();

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "200 derivations would take roughly twenty seconds; these must be cache hits");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AWrongPasswordIsNeverRememberedAsAcceptable()
    {
        // Only successes are cached. Caching a refusal would be harmless; caching it as an
        // acceptance would not, and the two live one line apart.
        ConsoleAccessGate gate = Gate();
        string wrong = Basic("operator", "not-the-password");

        for (int i = 0; i < 5; i++) gate.Allows(wrong).Should().BeFalse();

        gate.Allows(Basic("operator", Secret)).Should().BeTrue();
        gate.Allows(wrong).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void APasswordThatIsAPrefixOfTheRealOneIsNotEnough()
    {
        Gate().Allows(Basic("operator", Secret[..^1])).Should().BeFalse();
        Gate().Allows(Basic("operator", Secret + "x")).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier3")]
    public void AColonInsideThePasswordSurvivesTheHeader()
    {
        // Basic splits on the first colon, so a password containing one must still arrive whole.
        // Getting this wrong locks an operator out of their own console with no clue why.
        const string awkward = "pass:with:colons";
        var gate = new ConsoleAccessGate(PasswordCredential.Create(awkward));

        gate.Allows(Basic("operator", awkward)).Should().BeTrue();
    }
}
