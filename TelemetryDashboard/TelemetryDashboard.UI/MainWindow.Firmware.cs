using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.UI;

/// <summary>
/// Describing this installation to the firmware generator.
/// </summary>
/// <remarks>
/// The generator dialog was written to take a real configuration and to say on screen when it had
/// none — and nothing ever gave it one, so every operator who opened it got the worked example:
/// node <c>STM32_MCU_NODE_1</c> and three invented channels. The code was correct and about
/// somebody else's machine.
/// <para>
/// Everything here comes from something the operator actually set. The channels are the active
/// profile's, the baud rate is the one selected beside the port, and the tag is the one this
/// product's own router matches on — so the generated firmware is readable by the dashboard that
/// generated it rather than by a convention nobody configured.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>
    /// The active installation as firmware configuration, or null when there is nothing to describe.
    /// </summary>
    /// <remarks>
    /// Null rather than a half-filled config when no profile is loaded. A profile is where the
    /// channel list comes from, and a node configuration with no channels generates a struct with
    /// the example's temperature and vibration in it — which is the fabrication this replaces.
    /// </remarks>
    private SensorNodeConfig? FirmwareConfig()
    {
        MonitoringProfile? profile = _activeProfile;
        if (profile is null || profile.Channels.Count == 0) return null;

        return new SensorNodeConfig
        {
            // The profile's first declared node, which is the box the firmware runs on. Profiles
            // that declare none fall back to the profile's own id rather than to a literal.
            NodeId = profile.Nodes.Count > 0 ? profile.Nodes[0].Id : profile.Id,
            BaudRate = SelectedBaudRate(),
            TagPrefix = DefaultRoutingRules.TelemetryTag,
            Variables = profile.Channels.Select(channel => new VariableDefinition
            {
                Name = channel.Id,
                Unit = channel.Unit,
                DataType = "float"
            }).ToList()
        };
    }

    /// <summary>The baud rate selected beside the port, or the default when none is.</summary>
    /// <remarks>
    /// One reading of the combo box for the connect path and the generator both. Two readings drift,
    /// and the way this one would drift is firmware told to speak at a rate the dashboard does not
    /// listen at — which presents as a dead link with both sides configured correctly on screen.
    /// </remarks>
    private int SelectedBaudRate()
    {
        if (CboBaudRate.SelectedItem is int selected) return selected;

        return int.TryParse(CboBaudRate.SelectedItem?.ToString(), out int parsed)
            ? parsed
            : DefaultBaudRate;
    }

    /// <summary>Baud rate assumed when nothing has been selected.</summary>
    public const int DefaultBaudRate = 115200;
}
