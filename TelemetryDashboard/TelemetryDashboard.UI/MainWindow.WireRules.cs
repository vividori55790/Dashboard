using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.UI.Services;

namespace TelemetryDashboard.UI;

/// <summary>
/// What this installation's device calls its channels.
/// </summary>
/// <remarks>
/// The headless host could be told, through <c>--rules</c>, and this window could not be told at
/// all: it registered the built-in framing and nothing else, so the desktop — which is what most
/// operators actually run — could only read a device that already spoke this product's own
/// generated firmware. A real STM32 sends its own names in its own units, and every band, computed
/// channel and twin placement the profile declares matched nothing while the charts filled up.
/// <para>
/// Two buttons, because a rename is useless without a way to learn the names. One loads a rules
/// file; the other listens to whatever is arriving and writes the file to start from.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>Set while the draft button is listening. See <c>ResolvePackets</c>.</summary>
    private WireSurvey? _wireSurvey;

    private static readonly TimeSpan DraftWindow = TimeSpan.FromSeconds(15);

    /// <summary>Whether a rules file has actually been put in force this session.</summary>
    /// <remarks>
    /// Not the same question as "a file is configured", and the difference showed up on the running
    /// window: the profile picker is filled during construction, before the stored file is read, so
    /// a re-audit gated on the setting reported all four declared channels unmapped a moment before
    /// the file that maps them was loaded. Two contradictory lines, the wrong one first.
    /// </remarks>
    private bool _wireRulesInForce;

    /// <summary>Loads whatever file was chosen last time, at start-up.</summary>
    private void ApplyStoredWireRules()
    {
        string path = _uiSettings.WireRulesPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        if (!File.Exists(path))
        {
            // Said out loud rather than silently falling back. The operator configured a mapping;
            // running without it looks identical to running with it and is not the same thing.
            ControlPanel.LogMessage("WIRE",
                $"The wire rules file '{path}' is gone, so this session is reading the built-in "
                + "framing. Channels will arrive under whatever names the device uses.");
            return;
        }

        LoadWireRules(path, announcePath: true);
    }

    private void BtnLoadWireRules_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "장비 이름 매핑 파일 선택",
            Filter = "Wire rules (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (picker.ShowDialog(this) != true) return;

        LoadWireRules(picker.FileName, announcePath: true);
    }

    private void LoadWireRules(string path, bool announcePath)
    {
        RoutingRuleReader.Result read;
        try
        {
            read = RoutingRuleReader.Load(path);
        }
        catch (Exception failure) when (failure is InvalidDataException or IOException
                                        or UnauthorizedAccessException or FileNotFoundException)
        {
            // Refused, and the old rules stay in force. Half-applying a rule set would leave the
            // operator unable to say which names are currently in effect.
            ControlPanel.LogMessage("WIRE", $"'{Path.GetFileName(path)}' was not applied: {failure.Message}");
            return;
        }

        _dataRouter.ReplaceRules(read.Rules);
        _wireRulesInForce = true;
        _uiSettings.WireRulesPath = path;
        _uiSettings.Save();

        if (announcePath)
        {
            ControlPanel.LogMessage("WIRE",
                $"{read.Rules.Count} rule(s) from {Path.GetFileName(path)} are now in force, "
                + "replacing the built-in framing.");
        }

        foreach (string warning in read.Warnings) ControlPanel.LogMessage("WIRE", warning);

        AuditWireRules(read.Rules);
    }

    /// <summary>
    /// Re-judges the rules in force against a profile the operator has just switched to.
    /// </summary>
    /// <remarks>
    /// The same mapping means different things under different profiles: a file naming
    /// psfb.output_voltage is complete for a converter and maps nothing at all for a generic
    /// machine. Saying so at the moment of the switch is the only time the operator is looking.
    /// </remarks>
    private void AuditWireRulesForNewProfile()
    {
        if (!_wireRulesInForce) return;

        AuditWireRules([.. _dataRouter.Rules]);
    }

    /// <summary>Says which mappings the active profile will and will not act on.</summary>
    /// <remarks>
    /// Only meaningful for a rules file. The built-in framing renames nothing, so every declared
    /// channel would be reported as unmapped while the device may be sending those very names.
    /// </remarks>
    private void AuditWireRules(IReadOnlyList<RoutingRule> rules)
    {
        if (_activeProfile is null) return;

        foreach (string finding in RoutingRuleAudit.Check(rules, _activeProfile))
        {
            ControlPanel.LogMessage("WIRE", finding);
        }

        IReadOnlyList<string> silent = RoutingRuleAudit.Unmapped(rules, _activeProfile);
        if (silent.Count == 0) return;

        ControlPanel.LogMessage("WIRE",
            $"{silent.Count} declared channel(s) have no mapping: {string.Join(", ", silent)}. "
            + "Nothing will judge them until something arrives under those names.");
    }
}
