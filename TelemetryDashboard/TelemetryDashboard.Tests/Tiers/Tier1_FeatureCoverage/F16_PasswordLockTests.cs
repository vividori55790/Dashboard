using System;
using System.IO;
using TelemetryDashboard.Core.Security;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

/// <summary>
/// The screen lock's credential, attempt policy and store.
/// </summary>
/// <remarks>
/// This file used to test <c>PasswordGuardHelper</c>, a 28-line class declared at the bottom of
/// this same file, which reimplemented the lock's logic well enough to pass five assertions about
/// it. Nothing it tested shipped. The product's own service was never constructed here, and the
/// suite reported F16 as covered — so the shipping code could have been deleted without turning
/// anything red.
/// <para>
/// What the shipping code actually did, and what these tests now make impossible: it compared the
/// input against the literal <c>admin123</c>. Not as a seed to be replaced on first run — as the
/// credential, on every launch, because the field holding the hash was per-process and started
/// null. A second copy of the same literal sat in the lock overlay and would accept it with no
/// service attached at all.
/// </para>
/// </remarks>
public class F16_PasswordLockTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "tdcred_" + Guid.NewGuid().ToString("N")[..10] + ".cred");

    // ---- the credential -----------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheStoredFormCarriesNoTraceOfThePassword()
    {
        PasswordCredential credential = PasswordCredential.Create("correct horse battery");

        string stored = credential.ToStorage();

        stored.Should().NotContain("correct horse battery");
        stored.Should().StartWith("v1$", "the format states its version so it can be changed later");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ThePasswordThatWasSetIsTheOneThatOpensIt()
    {
        PasswordCredential credential = PasswordCredential.Create("plant-floor-42");

        credential.Verify("plant-floor-42").Should().BeTrue();
        credential.Verify("plant-floor-43").Should().BeFalse();
        credential.Verify("").Should().BeFalse();
        credential.Verify(null).Should().BeFalse();
    }

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData("admin123")]
    [InlineData("admin")]
    [InlineData("password")]
    public void NoPasswordOpensACredentialItWasNotSetFrom(string guess)
    {
        // admin123 by name, because it was the real credential of every installation and would be
        // again the moment anyone reintroduced a default. A test that names it fails loudly.
        PasswordCredential credential = PasswordCredential.Create("something-else-entirely");

        credential.Verify(guess).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TwoInstallationsWithTheSamePasswordStoreDifferentThings()
    {
        // A per-credential salt. Without it, identical stored values across machines say "these two
        // sites chose the same password", and one precomputed table covers every installation.
        string a = PasswordCredential.Create("identical-password").ToStorage();
        string b = PasswordCredential.Create("identical-password").ToStorage();

        a.Should().NotBe(b);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void APasswordTooShortToBeOneIsRefusedWhereItIsSet()
    {
        Action create = () => PasswordCredential.Create("short");

        create.Should().Throw<ArgumentException>()
            .WithMessage($"*{PasswordCredential.MinimumLength}*");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ACredentialSurvivesBeingWrittenDownAndReadBack()
    {
        PasswordCredential original = PasswordCredential.Create("round-trip-me");

        PasswordCredential.TryParse(original.ToStorage(), out PasswordCredential restored)
            .Should().BeTrue();
        restored.Verify("round-trip-me").Should().BeTrue();
        restored.Verify("round-trip-you").Should().BeFalse();
    }

    [Theory]
    [Trait("Category", "Tier1")]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("v1$210000$notbase64$alsonot")]
    [InlineData("v1$210000$AAAA$AAAA")]        // right shape, wrong lengths
    [InlineData("v2$210000$AAAA$AAAA")]        // a version this build does not know
    public void AStoredValueThatIsNotOneIsRefusedRatherThanRepaired(string stored)
    {
        // The failure mode of a lock is that it opens. A truncated or hand-edited file must not
        // become a credential that accepts something.
        PasswordCredential.TryParse(stored, out _).Should().BeFalse();
    }

    // ---- the attempt policy -------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void WithNoPasswordSetThereIsNothingToCheck()
    {
        // The state every real launch used to be in, and the one no test covered.
        var policy = new ScreenLockPolicy();

        policy.IsConfigured.Should().BeFalse();
        policy.Authenticate("admin123", DateTime.UtcNow).Should().Be(UnlockOutcome.NotConfigured);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void WrongAnswersAreCountedAndThenMadeToWait()
    {
        var policy = new ScreenLockPolicy(
            PasswordCredential.Create("the-real-one"), attemptsPerRound: 3,
            firstCooldown: TimeSpan.FromSeconds(30));
        DateTime t = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

        policy.Authenticate("nope", t).Should().Be(UnlockOutcome.Rejected);
        policy.RemainingAttempts.Should().Be(2);
        policy.Authenticate("nope", t).Should().Be(UnlockOutcome.Rejected);
        policy.Authenticate("nope", t).Should().Be(UnlockOutcome.CoolingDown);

        // Even the right password waits. Otherwise the cooldown is only a delay for people who
        // cannot guess.
        policy.Authenticate("the-real-one", t).Should().Be(UnlockOutcome.CoolingDown);
        policy.CooldownRemaining(t).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TheWaitEndsAndTheOperatorGetsBackIn()
    {
        // The defect this replaces: three wrong answers set a flag that nothing ever cleared, so
        // an operator who mistyped was locked out of their own dashboard until they killed the
        // process. No test noticed, because no test tried a fourth time.
        var policy = new ScreenLockPolicy(
            PasswordCredential.Create("the-real-one"), attemptsPerRound: 2,
            firstCooldown: TimeSpan.FromSeconds(10));
        DateTime t = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

        policy.Authenticate("nope", t);
        policy.Authenticate("nope", t).Should().Be(UnlockOutcome.CoolingDown);

        policy.Authenticate("the-real-one", t.AddSeconds(11)).Should().Be(UnlockOutcome.Unlocked);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void EachRoundOfGuessingCostsLongerThanTheLast()
    {
        var policy = new ScreenLockPolicy(
            PasswordCredential.Create("the-real-one"), attemptsPerRound: 1,
            firstCooldown: TimeSpan.FromSeconds(10), maximumCooldown: TimeSpan.FromSeconds(60));
        DateTime t = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

        policy.Authenticate("a", t);
        policy.CooldownRemaining(t).Should().Be(TimeSpan.FromSeconds(10));

        t = t.AddSeconds(11);
        policy.Authenticate("b", t);
        policy.CooldownRemaining(t).Should().Be(TimeSpan.FromSeconds(20));

        t = t.AddSeconds(21);
        policy.Authenticate("c", t);
        policy.CooldownRemaining(t).Should().Be(TimeSpan.FromSeconds(40));

        // And it stops growing, so a mistyped password can never cost an hour.
        t = t.AddSeconds(41);
        policy.Authenticate("d", t);
        policy.CooldownRemaining(t).Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void SettingAPasswordClearsTheWait()
    {
        var policy = new ScreenLockPolicy(
            PasswordCredential.Create("old-password"), attemptsPerRound: 1);
        DateTime t = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        policy.Authenticate("nope", t);

        policy.SetCredential(PasswordCredential.Create("new-password"));

        policy.CooldownRemaining(t).Should().Be(TimeSpan.Zero);
        policy.Authenticate("new-password", t).Should().Be(UnlockOutcome.Unlocked);
    }

    // ---- the store ----------------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void APasswordSetInOneSessionIsStillSetInTheNext()
    {
        // The whole point. The hash used to live in a field on a service created with the window,
        // so a password set in one session was gone in the next -- which left the compiled-in
        // literal as the only credential that ever actually applied.
        string path = TempPath();
        try
        {
            CredentialFile.Save(path, PasswordCredential.Create("survives-restart")).Should().BeNull();

            PasswordCredential? reloaded = CredentialFile.Load(path);

            reloaded.Should().NotBeNull();
            reloaded!.Verify("survives-restart").Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AMissingOrDamagedStoreReadsAsNoPasswordRatherThanAsAnError()
    {
        string path = TempPath();
        CredentialFile.Load(path).Should().BeNull("nothing has been written there");

        try
        {
            File.WriteAllText(path, "this is not a credential");
            CredentialFile.Load(path).Should().BeNull("a damaged file must not become a credential");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
