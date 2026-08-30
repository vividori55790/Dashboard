using System.Text.Json;
using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Services;

namespace TelemetryDashboard.Tests;

/// <summary>
/// ROADMAP W3: the generated dashboard must not draw a panel for a channel that has never reported.
/// </summary>
/// <remarks>
/// These assert against the serialised JSON rather than against the object graph, because the
/// object graph is anonymous types and what an operator imports is the text. A field renamed by
/// System.Text.Json, or one that serialises as <c>{}</c> because its declared type erased it, is a
/// dashboard that imports and draws nothing — and it would pass every test written against the
/// objects.
/// </remarks>
public class GrafanaDashboardExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 9, 0, 0, TimeSpan.Zero);

    private static JsonElement Export(InputInventory? inventory) =>
        JsonSerializer.SerializeToDocument(GrafanaDashboardExport.Build(inventory, Now)).RootElement;

    /// <summary>An inventory that has observed one reading on each named channel.</summary>
    private static InputInventory Heard(params (string Port, string Node, string Channel, string Unit)[] inputs)
    {
        var inventory = new InputInventory();
        foreach ((string port, string node, string channel, string unit) in inputs)
        {
            inventory.Observe(
                new RawPacket { PortName = port },
                new TelemetryPacket
                {
                    NodeId = node,
                    Variable = channel,
                    Unit = unit,
                    Value = 1.0,
                    Timestamp = Now.UtcDateTime
                });
        }
        return inventory;
    }

    private static JsonElement[] PanelsOfType(JsonElement dashboard, string type) =>
        dashboard.GetProperty("panels").EnumerateArray()
            .Where(p => p.GetProperty("type").GetString() == type)
            .ToArray();

    [Fact]
    [Trait("Category", "Tier1")]
    public void OnlyAChannelThatHasReportedGetsAPanel()
    {
        // The rule the whole workstream is judged against. The declared sets this host also has to
        // hand -- computed expressions, limit rules -- name channels that may never have produced a
        // value, and generating from one of those is the obvious wrong implementation.
        JsonElement dashboard = Export(Heard(
            ("COM3", "RIG-1", "dab.bus_voltage", "V"),
            ("COM3", "RIG-1", "dab.current", "A")));

        string[] titles = PanelsOfType(dashboard, "timeseries")
            .Select(p => p.GetProperty("title").GetString()!)
            .ToArray();

        titles.Should().BeEquivalentTo(["dab.bus_voltage [V]", "dab.current [A]"]);
        dashboard.GetProperty("panels").GetArrayLength().Should().Be(3, "two channels plus one row");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AHostThatKeepsNoInventorySaysSoRatherThanDrawingNothing()
    {
        // An empty dashboard and a dashboard that says why are the same file to a parser and
        // different facts to an operator. ARCHITECTURE §1, at the boundary.
        JsonElement dashboard = Export(null);

        PanelsOfType(dashboard, "timeseries").Should().BeEmpty();

        JsonElement[] text = PanelsOfType(dashboard, "text");
        text.Should().ContainSingle();
        text[0].GetProperty("options").GetProperty("content").GetString()
            .Should().Contain("아무도 세고 있지 않다",
                "'nobody is counting' is the fact here, and it is not 'there are no channels'");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TrackingWithNothingHeardIsADifferentSentenceFromNotTracking()
    {
        // The two silences. Collapsing them is the failure this product is organised against, and
        // an exported artefact leaves the console behind -- so the distinction has to survive
        // inside the file.
        string tracking = PanelsOfType(Export(new InputInventory()), "text")[0]
            .GetProperty("options").GetProperty("content").GetString()!;
        string untracked = PanelsOfType(Export(null), "text")[0]
            .GetProperty("options").GetProperty("content").GetString()!;

        tracking.Should().NotBe(untracked);
        tracking.Should().Contain("아직 하나도 도착하지 않았습니다");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void EveryPanelCarriesAUnitIdentifierGrafanaActuallyKnows()
    {
        // Grafana's units are a closed vocabulary; an invented id silently falls back to a bare
        // number, so "the axis is correct" is a claim only a real id can support.
        JsonElement dashboard = Export(Heard(
            ("COM3", "RIG-1", "bus", "V"),
            ("COM3", "RIG-1", "shunt", "mV"),
            ("COM3", "RIG-1", "coolant", "°C"),
            ("COM3", "RIG-1", "duty", "%"),
            ("COM3", "RIG-1", "spindle", "rpm")));

        string[] units = PanelsOfType(dashboard, "timeseries")
            .Select(p => p.GetProperty("fieldConfig").GetProperty("defaults")
                          .GetProperty("unit").GetString()!)
            .ToArray();

        units.Should().BeEquivalentTo(["volt", "mvolt", "celsius", "percent", "rotrpm"]);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AUnitNobodyRecognisesBecomesANumberRatherThanAGuess()
    {
        // W1's rule at the one place a wrong answer becomes an axis label read as fact. "g" is the
        // live example: Grafana has both accG and massg and nothing on the wire separates them.
        GrafanaDashboardExport.UnitId("g").Should().Be(GrafanaDashboardExport.UnitlessNumber);
        GrafanaDashboardExport.UnitId("smoots").Should().Be(GrafanaDashboardExport.UnitlessNumber);
        GrafanaDashboardExport.UnitId("").Should().Be(GrafanaDashboardExport.UnitlessNumber);

        // And the panel says which of the two it was, rather than presenting an unread unit as an
        // absent one.
        JsonElement dashboard = Export(Heard(("COM3", "RIG-1", "vibe", "g")));
        PanelsOfType(dashboard, "timeseries")[0].GetProperty("description").GetString()
            .Should().Contain("not one of Grafana's");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void UnitsAreMatchedCaseSensitivelyBecauseMilliAndMegaDiffer()
    {
        // A case-insensitive lookup makes mV and MV the same unit, which is a factor of a billion.
        // UnitScale already refuses to infer across that boundary for the same reason.
        GrafanaDashboardExport.UnitId("mV").Should().Be("mvolt");
        GrafanaDashboardExport.UnitId("MV").Should().Be(GrafanaDashboardExport.UnitlessNumber);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AChannelIsSelectedByNodeAndNameTogether()
    {
        // ARCHITECTURE §2: a channel name is unique only within the node that observed it, so a
        // query naming the channel alone plots two machines' sensors as one series.
        JsonElement dashboard = Export(Heard(
            ("COM3", "RIG-1", "TEMP", "°C"),
            ("COM4", "RIG-2", "TEMP", "°C")));

        string[] exprs = PanelsOfType(dashboard, "timeseries")
            .Select(p => p.GetProperty("targets")[0].GetProperty("expr").GetString()!)
            .ToArray();

        exprs.Should().HaveCount(2);
        exprs.Should().OnlyContain(e => e.Contains("channel=\"TEMP\""));
        exprs.Should().Contain(e => e.Contains("node=\"RIG-1\""));
        exprs.Should().Contain(e => e.Contains("node=\"RIG-2\""));
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ADeviceCannotBreakOutOfAQueryWithAQuoteInItsChannelName()
    {
        // §7: a channel name comes off a wire and is not trusted. An unescaped quote does not
        // break its own panel, it silently reselects.
        JsonElement dashboard = Export(Heard(("COM3", "RIG-1", "a\"} or up{", "V")));

        string expr = PanelsOfType(dashboard, "timeseries")[0]
            .GetProperty("targets")[0].GetProperty("expr").GetString()!;

        expr.Should().Contain("a\\\"} or up{");
        expr.Should().EndWith("\"}", "the matcher must still be the one this generator wrote");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheDashboardCarriesWhatGrafanaNeedsToImportItWithoutEditing()
    {
        JsonElement dashboard = Export(Heard(("COM3", "RIG-1", "bus", "V")));

        dashboard.GetProperty("schemaVersion").GetInt32().Should().Be(41);
        dashboard.GetProperty("uid").GetString().Should().Be(GrafanaDashboardExport.DashboardUid);
        dashboard.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();

        // The datasource is a variable rather than a hard-coded uid, which is what lets this import
        // into a Grafana whose Prometheus this host has never heard of.
        JsonElement variable = dashboard.GetProperty("templating").GetProperty("list")[0];
        variable.GetProperty("type").GetString().Should().Be("datasource");
        variable.GetProperty("query").GetString().Should().Be("prometheus");

        JsonElement panel = PanelsOfType(dashboard, "timeseries")[0];
        panel.GetProperty("datasource").GetProperty("uid").GetString()
            .Should().Be("${" + variable.GetProperty("name").GetString() + "}");

        JsonElement grid = panel.GetProperty("gridPos");
        (grid.GetProperty("x").GetInt32() + grid.GetProperty("w").GetInt32())
            .Should().BeLessThanOrEqualTo(24, "Grafana's grid is 24 columns wide");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AGapIsDrawnAsAGapRatherThanAsALineThroughIt()
    {
        // With spanNulls on, Grafana joins across a window where the hub received nothing, and an
        // outage renders as a calm trend -- ARCHITECTURE's opening failure, in somebody else's tool.
        JsonElement dashboard = Export(Heard(("COM3", "RIG-1", "bus", "V")));

        PanelsOfType(dashboard, "timeseries")[0]
            .GetProperty("fieldConfig").GetProperty("defaults")
            .GetProperty("custom").GetProperty("spanNulls").GetBoolean()
            .Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheDescriptionSaysHowManyChannelsItWasBuiltFromAndWhatItAssumes()
    {
        // The provenance an envelope would normally carry, put where it survives the file being
        // saved and mailed to somebody -- including the /metrics contract, which is the assumption
        // most likely to be wrong and least likely to be noticed.
        string description = Export(Heard(("COM3", "RIG-1", "bus", "V")))
            .GetProperty("description").GetString()!;

        description.Should().Contain("1 channel");
        description.Should().Contain(GrafanaDashboardExport.MetricName);
        description.Should().Contain("actually heard from");
    }
}
