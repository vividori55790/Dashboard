using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using TelemetryDashboard.Core.Ingest;
using TelemetryDashboard.Core.Models;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Covers the projection that lets this hub read a feed it was not written for.
/// </summary>
/// <remarks>
/// The document shapes here are taken from real responses — a USGS GeoJSON summary and a Wikimedia
/// recent-change event — rather than invented, because the interesting cases in a third party's
/// JSON are the ones nobody would think to invent: a depth buried at index 2 of a coordinate array,
/// a number sent as a quoted string, a field that is simply absent from this particular event.
/// </remarks>
public class JsonChannelMapTests
{
    private static readonly DateTime Observed = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    private const string UsgsDocument = """
    {
      "type": "FeatureCollection",
      "metadata": { "title": "USGS All Earthquakes, Past Hour", "count": 13 },
      "features": [
        {
          "properties": { "mag": 1.3, "place": "Alaska" },
          "geometry": { "type": "Point", "coordinates": [-150.1, 63.2, 1.95000004768372] }
        }
      ]
    }
    """;

    private static JsonChannelMap UsgsMap() => new(
        "usgs",
        new[]
        {
            new JsonChannel("latest_magnitude", "features.0.properties.mag"),
            new JsonChannel("latest_depth_km", "features.0.geometry.coordinates.2", "km"),
            new JsonChannel("event_count", "metadata.count")
        },
        nodePath: "metadata.title",
        nodeFallback: "usgs");

    [Fact]
    public void EveryMappedPathIsExtractedFromARealDocument()
    {
        IReadOnlyList<TelemetryPacket> packets = UsgsMap().Project(UsgsDocument, Observed);

        packets.Should().HaveCount(3);
        packets.Select(p => p.Variable).Should()
            .BeEquivalentTo("latest_magnitude", "latest_depth_km", "event_count");

        packets.Single(p => p.Variable == "latest_magnitude").Value.Should().Be(1.3);
        packets.Single(p => p.Variable == "event_count").Value.Should().Be(13);
    }

    [Fact]
    public void AValueInsideAnArrayIsReachedByIndex()
    {
        // Depth is the third coordinate of a GeoJSON point. Without index support the channel is
        // simply absent, and an absent channel is indistinguishable from a sensor that went quiet.
        TelemetryPacket depth = UsgsMap().Project(UsgsDocument, Observed)
            .Single(p => p.Variable == "latest_depth_km");

        depth.Value.Should().BeApproximately(1.95, 1e-6);
        depth.Unit.Should().Be("km");
    }

    [Fact]
    public void TheNodeNameComesFromTheDocumentWhenThePathResolves()
    {
        UsgsMap().Project(UsgsDocument, Observed)
            .Should().OnlyContain(p => p.NodeId == "USGS All Earthquakes, Past Hour");
    }

    [Fact]
    public void AnAbsentPathProducesNoPacketRatherThanZero()
    {
        var map = new JsonChannelMap("partial", new[]
        {
            new JsonChannel("present", "metadata.count"),
            new JsonChannel("absent", "metadata.does_not_exist")
        });

        IReadOnlyList<TelemetryPacket> packets = map.Project(UsgsDocument, Observed);

        packets.Should().ContainSingle().Which.Variable.Should().Be("present");
        packets.Should().NotContain(p => p.Variable == "absent",
            "a zero would draw a cliff to the floor and every mean downstream would follow it");
    }

    [Fact]
    public void ANumberSentAsAQuotedStringIsStillANumber()
    {
        // Exchanges routinely quote prices to avoid float rounding in transit. Refusing those would
        // mean the map silently matched nothing on a perfectly healthy feed.
        var map = new JsonChannelMap("quoted", new[] { new JsonChannel("price", "price", "USD") });

        map.Project("""{"symbol":"BTCUSDT","price":"69042.97000000"}""", Observed)
            .Single().Value.Should().BeApproximately(69042.97, 1e-6);
    }

    [Fact]
    public void NonFiniteAndNonNumericValuesAreRefused()
    {
        var map = new JsonChannelMap("odd", new[]
        {
            new JsonChannel("text", "a"),
            new JsonChannel("nothing", "b"),
            new JsonChannel("flag", "c")
        });

        map.Project("""{"a":"not a number","b":null,"c":true}""", Observed).Should().BeEmpty(
            "an infinity or a string is not a reading, and admitting one poisons every rolling mean");
    }

    [Fact]
    public void DocumentsThatMatchNothingAreCountedSoAWrongMapIsVisible()
    {
        var map = new JsonChannelMap("mismatched", new[] { new JsonChannel("x", "nowhere.at.all") });

        map.Project(UsgsDocument, Observed).Should().BeEmpty();
        map.Project(UsgsDocument, Observed).Should().BeEmpty();

        // Without this counter a wrong map and a dead feed produce the identical symptom: silence.
        map.UnmatchedDocuments.Should().Be(2);
        map.MatchedDocuments.Should().Be(0);
    }

    [Fact]
    public void MalformedJsonIsCountedRatherThanThrown()
    {
        var map = new JsonChannelMap("broken", new[] { new JsonChannel("x", "a") });

        map.Project("{ this is not json", Observed).Should().BeEmpty();
        map.MalformedDocuments.Should().Be(1, "one bad frame must not end the run");
    }

    [Fact]
    public void AMapWithNoChannelsIsRefusedAtConstruction()
    {
        Action empty = () => _ = new JsonChannelMap("empty", Array.Empty<JsonChannel>());
        empty.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TwoChannelsWithTheSameNameAreRefused()
    {
        // Two paths writing one channel interleave two quantities into a series that looks like
        // noise, and nothing downstream can separate them again.
        Action duplicate = () => JsonChannelMapReader.Parse("""
        {
          "channels": [
            { "variable": "temp", "path": "a" },
            { "variable": "temp", "path": "b" }
          ]
        }
        """);

        duplicate.Should().Throw<InvalidDataException>().WithMessage("*declared twice*");
    }

    [Fact]
    public void AChannelWithoutAPathIsRefused()
    {
        Action pathless = () => JsonChannelMapReader.Parse("""
        { "channels": [ { "variable": "temp" } ] }
        """);

        pathless.Should().Throw<InvalidDataException>().WithMessage("*path*");
    }

    [Fact]
    public void TheShippedChannelMapsLoadAndDeclareWhatTheyClaim()
    {
        // Walk up to the solution directory rather than assuming a build layout.
        DirectoryInfo? here = new(AppContext.BaseDirectory);
        while (here is not null && !Directory.Exists(Path.Combine(here.FullName, "TelemetryDashboard.Host")))
        {
            here = here.Parent;
        }

        if (here is null) return;

        string directory = Path.Combine(here.FullName, "TelemetryDashboard.Host", "channel-maps");
        if (!Directory.Exists(directory)) return;

        Directory.EnumerateFiles(directory, "*.json").Should().NotBeEmpty(
            "the product ships example maps and a broken one would only surface in the field");

        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
        {
            JsonChannelMap map = JsonChannelMapReader.Load(file);
            map.Channels.Should().NotBeEmpty($"{Path.GetFileName(file)} ships with the product");
            map.Channels.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Path));
        }
    }
}
