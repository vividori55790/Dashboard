using TelemetryDashboard.UI.Controls;

namespace TelemetryDashboard.Tests.Desktop;

/// <summary>
/// The single WPF-bound member of the M2 enterprise verification set.
/// </summary>
/// <remarks>
/// <c>EventLogEntry</c> is declared in <c>ControlPanelControl.xaml.cs</c>, so touching it loads the
/// WPF shell. Its eleven siblings — the dashboard exporter, the DVR player, the incident report
/// generator and the streaming server — are pure Core and remain in the portable project rather
/// than being exiled to Windows by one row-model type.
/// </remarks>
public class MilestoneM2EventLogEntryTests
{
    [Fact]
    public void Test_EventLogEntry_PropertiesAndAlertLevels()
    {
        var entry = new EventLogEntry
        {
            Time = "14:32:05.123",
            Level = "CRIT",
            Node = "COM3",
            Variable = "Temp",
            Value = "104.2 °C",
            ZScore = "3.8σ",
            Message = "CRITICAL Thermal Anomaly"
        };

        entry.Time.Should().Be("14:32:05.123");
        entry.Level.Should().Be("CRIT");
        entry.Node.Should().Be("COM3");
        entry.Variable.Should().Be("Temp");
        entry.Value.Should().Be("104.2 °C");
        entry.ZScore.Should().Be("3.8σ");
        entry.Message.Should().Be("CRITICAL Thermal Anomaly");
    }
}
