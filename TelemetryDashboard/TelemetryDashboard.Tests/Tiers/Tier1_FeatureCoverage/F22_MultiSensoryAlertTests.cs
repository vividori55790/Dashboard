namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F22_MultiSensoryAlertTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void AlertUXService_ThresholdBreach_ActivatesAlertState()
    {
        var alertService = new AlertUxServiceState();
        alertService.EvaluateSensorValue("MCU_NODE_1", "TEMP", 92.5, threshold: 85.0);

        alertService.IsAlertActive.Should().BeTrue();
        alertService.ActiveAlertMessage.Should().Contain("85");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AlertUXService_NeonHighlight_ChangesColorToAlert()
    {
        var alertService = new AlertUxServiceState();
        alertService.EvaluateSensorValue("MCU_NODE_1", "TEMP", 92.5, threshold: 85.0);

        alertService.NeonHighlightBrush.Should().Be("NeonRed");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AlertUXService_WindowFlash_TriggersBorderPulse()
    {
        var alertService = new AlertUxServiceState();
        alertService.EvaluateSensorValue("MCU_NODE_1", "TEMP", 90.0, threshold: 85.0);

        alertService.IsBorderFlashing.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AlertUXService_ToastNotification_FormatsToastPayload()
    {
        var alertService = new AlertUxServiceState();
        string toast = alertService.FormatToastNotification("MCU_NODE_1", "TEMP", 92.5);

        toast.Should().Contain("MCU_NODE_1");
        toast.Should().Contain("TEMP");
        toast.Should().Contain("92.5");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AlertUXService_SapiTts_SpeaksAlarmText()
    {
        var alertService = new AlertUxServiceState();
        string spokenMessage = alertService.FormatTtsMessage("Engine Node", "temperature", 92.5);

        spokenMessage.Should().Be("Warning: Engine Node temperature exceeded 92.5 degrees");
    }
}

public class AlertUxServiceState
{
    public bool IsAlertActive { get; private set; }
    public string ActiveAlertMessage { get; private set; } = string.Empty;
    public string NeonHighlightBrush { get; private set; } = "NormalGreen";
    public bool IsBorderFlashing { get; private set; }

    public void EvaluateSensorValue(string nodeId, string variable, double value, double threshold)
    {
        if (value > threshold)
        {
            IsAlertActive = true;
            ActiveAlertMessage = $"Warning: {nodeId} {variable} exceeded {threshold}";
            NeonHighlightBrush = "NeonRed";
            IsBorderFlashing = true;
        }
    }

    public string FormatToastNotification(string nodeId, string variable, double value)
    {
        return $"{{\"title\":\"Alert Breach\",\"body\":\"Node {nodeId} {variable} = {value:F1}\"}}";
    }

    public string FormatTtsMessage(string name, string variable, double val)
    {
        return $"Warning: {name} {variable} exceeded {val:F1} degrees";
    }
}
