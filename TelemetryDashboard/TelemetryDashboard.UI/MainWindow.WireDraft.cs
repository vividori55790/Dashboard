using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TelemetryDashboard.Core.Ingest;

namespace TelemetryDashboard.UI;

public partial class MainWindow
{
    private DispatcherTimer? _draftTimer;

    /// <summary>
    /// Listens to whatever is arriving and writes the rules file to start from.
    /// </summary>
    /// <remarks>
    /// The other half of the mapping, and the half without which the first is unusable: a file that
    /// renames <c>Vout</c> to <c>psfb.output_voltage</c> can only be written by somebody who already
    /// knows the device sends <c>Vout</c>, in millivolts, on a <c>$TELE</c> frame. None of that is
    /// recorded anywhere except inside the device.
    /// <para>
    /// It observes the same router the charts are fed from, so what it writes down is what this
    /// session is actually receiving rather than a second opinion about the same port.
    /// </para>
    /// </remarks>
    private void BtnDraftWireRules_Click(object sender, RoutedEventArgs e)
    {
        if (_wireSurvey is not null) return;

        if (!_isConnected && !_isSimulating)
        {
            ControlPanel.LogMessage("WIRE",
                "Nothing is arriving to listen to. Connect the device, or start the simulator, and "
                + "then draft the rules.");
            return;
        }

        _wireSurvey = new WireSurvey();
        BtnDraftWireRules.IsEnabled = false;
        ControlPanel.LogMessage("WIRE",
            $"Listening for {DraftWindow.TotalSeconds:0}s. Every channel that arrives will be "
            + "written down; nothing is changed while this runs.");

        _draftTimer = new DispatcherTimer { Interval = DraftWindow };
        _draftTimer.Tick += (_, _) => FinishDraft();
        _draftTimer.Start();
    }

    private void FinishDraft()
    {
        _draftTimer?.Stop();
        _draftTimer = null;
        BtnDraftWireRules.IsEnabled = true;

        WireSurvey? survey = _wireSurvey;
        _wireSurvey = null;
        if (survey is null) return;

        ControlPanel.LogMessage("WIRE",
            $"Heard {survey.Lines:N0} line(s); {survey.Channels.Count} channel(s) arrived.");

        foreach (WireChannel channel in survey.Channels)
        {
            string unit = channel.Unit.Length > 0 ? channel.Unit : "no unit";
            ControlPanel.LogMessage("WIRE",
                $"  ${channel.Tag} {channel.NodeId} {channel.Name}: {unit}, {channel.Range}, "
                + $"{channel.Samples:N0} sample(s).");
        }

        if (survey.Lines == 0)
        {
            ControlPanel.LogMessage("WIRE",
                "Nothing arrived at all, so there is nothing to write down. That is the device or "
                + "the link rather than the mapping.");
            return;
        }

        SaveDraft(RuleDraft.Render(survey, _activeProfile, "the desktop shell, listening live"));
    }

    private void SaveDraft(string draft)
    {
        var picker = new SaveFileDialog
        {
            Title = "장비 이름 매핑 초안 저장",
            Filter = "Wire rules (*.json)|*.json",
            FileName = "rules.json",
            OverwritePrompt = true
        };

        if (picker.ShowDialog(this) != true)
        {
            ControlPanel.LogMessage("WIRE", "The draft was not saved.");
            return;
        }

        try
        {
            File.WriteAllText(picker.FileName, draft, Core.Services.Utf8Files.WithoutBom);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            ControlPanel.LogMessage("WIRE", $"The draft could not be written: {failure.Message}");
            return;
        }

        ControlPanel.LogMessage("WIRE",
            $"Wrote {Path.GetFileName(picker.FileName)}. Fill in the entries it left commented out, "
            + "then load it with the mapping button beside this one.");
    }
}
