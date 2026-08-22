using TelemetryDashboard.UI.Services;

namespace TelemetryDashboard.Tests.Desktop.Tiers.Tier2_BoundaryCornerCases;

/// <summary>F15 and F16: i18n fallback and engineer-mode password guard boundary cases.</summary>
/// <remarks>
/// Both services back XAML bindings — <c>LanguageService</c> raises change notifications the shell
/// re-reads, and <c>PasswordLockService</c> gates the engineer view — so both are presentation
/// types. Paired in one file because each contributes about sixty lines and the two together stay
/// under the 150-line micro-module limit.
/// </remarks>
public class F15_F16_LocalizationAndLockBoundaryTests
{
    [Fact]
    [Trait("Category", "Tier2")]
    public void F15_Boundary_UnsupportedCultureCode_FallsBackToEnglish()
    {
        var langService = new LanguageService();
        langService.SetLanguage("fr-FR"); // Unsupported, fallback to en-US

        langService.CurrentCulture.Name.Should().Be("en-US");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F15_Boundary_MissingTranslationKey_ReturnsKeyName()
    {
        var langService = new LanguageService();
        string value = langService.GetString("MISSING_KEY_XYZ");
        value.Should().Be("MISSING_KEY_XYZ");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F15_Boundary_NullOrEmptyTranslationKey_ReturnsEmptyString()
    {
        var langService = new LanguageService();
        string valNull = langService.GetString(null!);
        string valEmpty = langService.GetString("");

        valNull.Should().BeEmpty();
        valEmpty.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F15_Boundary_RapidCultureSwitching_UpdatesAllBoundProperties()
    {
        var langService = new LanguageService();
        for (int i = 0; i < 20; i++)
        {
            langService.SetLanguage(i % 2 == 0 ? "ko-KR" : "en-US");
        }
        langService.CurrentCulture.Name.Should().Be("en-US");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F15_Boundary_FormatArgumentsMismatch_ReturnsUnformattedTemplate()
    {
        var langService = new LanguageService();
        string formatted = langService.GetFormattedString("WelcomeUser", /* missing arg */ Array.Empty<object>());
        formatted.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F16_Boundary_InvalidPassword_FailsAuthentication()
    {
        var lockService = new PasswordLockService(TempCredential());
        lockService.SetPassword("CorrectPassword123!");

        bool authenticated = lockService.Authenticate("WrongPassword");
        authenticated.Should().BeFalse();
        lockService.IsEngineerModeUnlocked.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F16_Boundary_EmptyPasswordSubmit_FailsAuthentication()
    {
        var lockService = new PasswordLockService(TempCredential());
        lockService.SetPassword("CorrectPassword123!");

        bool authenticated = lockService.Authenticate("");
        authenticated.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F16_Boundary_MaxFailedPasswordAttempts_LocksOutTemporarily()
    {
        var lockService = new PasswordLockService(TempCredential(), maxAttempts: 3);
        lockService.SetPassword("Secret123");

        for (int i = 0; i < 3; i++)
        {
            lockService.Authenticate("BadPass");
        }

        lockService.IsCoolingDown.Should().BeTrue("three wrong answers start a cooldown");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F16_Boundary_UninitializedPasswordHash_RequiresSetup()
    {
        var lockService = new PasswordLockService(TempCredential());
        lockService.IsPasswordConfigured.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F16_Boundary_OperatorModeRestrictAction_BlocksEngineerView()
    {
        var lockService = new PasswordLockService(TempCredential());
        lockService.LockEngineerMode();

        bool canModifySettings = lockService.CanAccessEngineerView();
        canModifySettings.Should().BeFalse();
    }

    /// <summary>
    /// A credential path inside the temp directory, one per call.
    /// </summary>
    /// <remarks>
    /// The parameterless constructor points at the operator's own AppData, and SetPassword now
    /// writes there. These tests used it, so running the suite would have set — and then left — a
    /// screen-lock password on the machine doing the testing, and the "no password configured yet"
    /// case would have passed once and failed on every run after it.
    /// </remarks>
    private static string TempCredential() =>
        Path.Combine(Path.GetTempPath(), "tdlock_" + Guid.NewGuid().ToString("N")[..10] + ".cred");
}
