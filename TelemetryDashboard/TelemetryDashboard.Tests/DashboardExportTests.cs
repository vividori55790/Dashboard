using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Simulator;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The exported HTML console: what it describes, and what it is allowed to claim.
/// </summary>
/// <remarks>
/// Feature 6 in this project's inventory, marked Built since M2 and constructed by nothing, so the
/// page it produced had never been opened by anybody. Three faults had survived in it for that
/// reason, and each is pinned below: a connection chip whose text was the literal
/// <c>WS CONNECTED</c> with no code anywhere to change it; a widget that, on finding its field
/// missing from a packet, displayed the temperature instead and then zero; and a hardcoded port
/// 8080 in the script tag and the socket URL, so a host on any other port exported a page pointing
/// at nothing while asserting it was connected.
/// </remarks>
public class DashboardExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "tddash_" + Guid.NewGuid().ToString("N")[..8]);

    public DashboardExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Export(MonitoringProfile profile, int port = 8080)
    {
        string path = Path.Combine(_dir, "dash.html");
        new DashboardExporter().ExportCustomHtmlDashboard(
            path, profile.DisplayName, ProfileDashboardWidgets.For(profile), port);
        return File.ReadAllText(path);
    }

    [Fact]
    public void EveryDeclaredChannelGetsAReadingAndATrend()
    {
        MonitoringProfile profile = MonitoringProfileLibrary.Generic;

        IReadOnlyList<WidgetConfig> widgets = ProfileDashboardWidgets.For(profile);

        widgets.Should().HaveCount(profile.Channels.Count * 2);
        widgets.Select(w => w.Field).Distinct().Should()
            .BeEquivalentTo(profile.Channels.Select(c => c.Id));
    }

    [Fact]
    public void EachCardCarriesItsOwnChannelsUnitAndRange()
    {
        var profile = new MonitoringProfile
        {
            Id = "kiln", DisplayName = "Kiln",
            Channels =
            [
                new ProfileChannel { Id = "zone3.temp", Label = "Zone 3", Unit = "C", Minimum = 200, Maximum = 1400 }
            ]
        };

        WidgetConfig card = ProfileDashboardWidgets.For(profile)[0];

        card.Unit.Should().Be("C");
        card.MinLimit.Should().Be(200);
        card.MaxLimit.Should().Be(1400);
        card.WidgetType.Should().Be("gauge_meter", "a channel that declares a range can fill a bar");
    }

    [Fact]
    public void AChannelWithNoRangeGetsAReadoutRatherThanAGaugeAgainstInventedBounds()
    {
        var profile = new MonitoringProfile
        {
            Id = "rig", DisplayName = "Rig",
            Channels = [new ProfileChannel { Id = "count", Label = "Count", Unit = "", Minimum = 0, Maximum = 0 }]
        };

        ProfileDashboardWidgets.For(profile)[0].WidgetType.Should().Be("digital_card");
    }

    [Fact]
    public void TheExportedPageNeverClaimsAConnectionItHasNotMade()
    {
        string html = Export(MonitoringProfileLibrary.Generic);

        // The literal that used to sit in the markup, updated by nothing, so the chip read
        // "WS CONNECTED" over a page whose host was not running.
        html.Should().NotContain("WS CONNECTED (:",
            "the chip has to follow the socket rather than assert a state at build time");

        html.Should().Contain("onStatusChange",
            "the client already reported CONNECTED/DISCONNECTED/ERROR; the page simply never asked");
    }

    [Fact]
    public void AMissingFieldIsNeverFilledInFromAnotherQuantity()
    {
        string html = Export(MonitoringProfileLibrary.Generic);

        // data[w.Field] !== undefined ? data[w.Field] : (data.temp || 0)
        html.Should().NotContain("data.temp",
            "a card headed with one quantity must not display another one's reading, or a "
            + "confident zero, when its own channel is absent from the packet");
    }

    [Fact]
    public void ThePageAddressesTheHostThatExportedIt()
    {
        string html = Export(MonitoringProfileLibrary.Generic, port: 9137);

        html.Should().Contain("ws://localhost:9137/ws");
        html.Should().Contain("http://localhost:9137/telemetry-client.js");
        html.Should().NotContain(":8080", "8080 was hardcoded in three places");
    }

    [Fact]
    public void TwoProfilesProduceDashboardsDescribingDifferentSystems()
    {
        string generic = Export(MonitoringProfileLibrary.Generic);
        string power = Export(MonitoringProfileLibrary.PowerConverterUps);

        foreach (ProfileChannel channel in MonitoringProfileLibrary.PowerConverterUps.Channels)
        {
            generic.Should().NotContain($"\"Field\": \"{channel.Id}\"");
            power.Should().Contain($"\"Field\": \"{channel.Id}\"");
        }
    }

    [Fact]
    public void TheDefaultIsTheNeutralProfileRatherThanOneInstallationsHardware()
    {
        string path = Path.Combine(_dir, "default.html");
        new DashboardExporter().ExportCustomHtmlDashboard(path, "Default");
        string html = File.ReadAllText(path);

        // The list that used to be here named "Edge Temp Sensor (CH-1)" and a field called vin —
        // fields this wire format does not even carry, so all five cards would have sat at their
        // placeholder forever.
        html.Should().NotContain("Edge Temp Sensor");
        html.Should().NotContain("\"Field\": \"vin\"");
        html.Should().Contain($"\"Field\": \"{MonitoringProfileLibrary.Generic.Channels[0].Id}\"");
    }
}
