using TelemetryDashboard.UI.ViewModels;

namespace TelemetryDashboard.Tests.Desktop.Tiers.Tier2_BoundaryCornerCases;

/// <summary>F22: multi-sensory alert UX boundary cases.</summary>
/// <remarks>
/// <c>AlertUXService</c> speaks through System.Speech's SAPI synthesiser, which exists only on
/// Windows, so these cases cannot follow the rest of the alerting pipeline into the portable suite.
/// </remarks>
public class F22_MultiSensoryAlertBoundaryTests
{
    [Fact]
    [Trait("Category", "Tier2")]
    public void F22_Boundary_EmptyAlertMessage_SuppressesVoiceAlert()
    {
        var alertService = new AlertUXService();
        bool result = alertService.TriggerVoiceAlert("");
        result.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F22_Boundary_SapiTtsUnavailable_FallsBackToSilentToast()
    {
        var alertService = new AlertUXService();
        alertService.DisableSapiTts();

        bool result = alertService.TriggerAlert("Engine overheat", isCritical: true);
        result.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F22_Boundary_RapidAlertSpam_ThrottlesVoiceQueue()
    {
        var alertService = new AlertUXService();
        for (int i = 0; i < 50; i++)
        {
            alertService.TriggerVoiceAlert($"Alert number {i}");
        }

        alertService.PendingVoiceCount.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F22_Boundary_SpecialCharsInAlertText_SanitizesForSpeech()
    {
        var alertService = new AlertUXService();
        string clean = alertService.SanitizeSpeechText("Warning: <Temp> & High 'Voltage' @ 100%!");
        clean.Should().NotContain("<").And.NotContain(">");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void F22_Boundary_NegativeThresholdBreach_TriggersMinAlert()
    {
        var alertService = new AlertUXService();
        alertService.SetThresholds("TEMP", min: -40.0, max: 85.0);

        bool breached = alertService.EvaluateThreshold("TEMP", -50.0);
        breached.Should().BeTrue();
    }
}
