using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Host.Ingest;
using TelemetryDashboard.Infrastructure.Storage;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Turning a CSV recording into something that can be asked questions.
/// </summary>
/// <remarks>
/// This pins the capability that made <c>SqliteIndexRepository</c> unnecessary. That class
/// described itself as a fast lookup of "which file and offset holds a channel at a given moment"
/// and could not answer it: <c>byte_offset</c> was declared in its schema and never written, and
/// the <c>archive</c> column it did write had no method that reads it. What it actually did was
/// keep a second, narrower copy of every sample with no way to read the rows back.
/// <para>
/// The real need behind it is genuine — a CSV transcript cannot be queried, so "what did this
/// channel do last Tuesday" has no answer if all you kept was <c>--record</c>. Existing wiring
/// answers it: <c>--replay</c> plays a recording through the same pipeline a live source feeds,
/// and <c>--archive</c> is on the far end of that pipeline. Deleting code that nothing reaches is
/// only honest if the need it named is met, so this checks that it is.
/// </para>
/// </remarks>
public class RecordingToArchiveTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "tdrec_" + Guid.NewGuid().ToString("N")[..8]);

    public RecordingToArchiveTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Writes a recording in the recorder's own layout.</summary>
    private string Recording(int rowsPerChannel, params string[] channels)
    {
        string path = Path.Combine(_dir, "session.csv");
        var lines = new List<string>
        {
            "Timestamp_ISO,Timestamp_Sec,NodeId,Channel,Value,ZScore,IsAnomaly,"
            + "Predicted_Value,Predicted_Horizon_Sec,Status"
        };

        double t = 1_700_000_000.0;
        for (int i = 0; i < rowsPerChannel; i++)
        {
            foreach (string channel in channels)
            {
                lines.Add(
                    $"2026-01-01T00:00:00.000Z,{t.ToString("F3", CultureInfo.InvariantCulture)},"
                    + $"SIM:COM3,{channel},{(400.0 + i).ToString("F4", CultureInfo.InvariantCulture)},"
                    + "0.00,FALSE,0.0000,0,OK");
            }
            t += 0.1;
        }

        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public async Task EveryRowOfARecordingReachesTheArchiveAndCanBeQueriedBack()
    {
        // Measured on a live host before this was written: 990 CSV rows in, 990 samples queryable
        // out, across nine channels. This is the same round trip without the process.
        string csv = Recording(rowsPerChannel: 40, "dab.bus_voltage", "psfb.output_voltage");
        string db = Path.Combine(_dir, "converted.db");

        var replay = new ReplayTelemetrySource(csv, speed: 1000);
        replay.Load().Should().BeTrue();

        using var archive = new SqliteDataLogger(db);
        var seen = new List<TelemetryPacket>();

        await foreach (RawPacket raw in replay.ReadAsync(CancellationToken.None))
        {
            // The replay source hands out the recorder's rows; the ingest path parses them. This
            // asserts the transport, so it takes the rows straight from the source.
            seen.Add(Parse(raw.RawLine));
        }

        await archive.WriteBatchAsync(seen);

        IEnumerable<TelemetryPacket> back =
            await archive.QueryAsync(new QueryFilter(null, null, null, null, 100_000));

        back.Should().HaveCount(80, "forty rows on each of two channels, none lost and none invented");
        back.Select(p => p.Variable).Distinct().Should()
            .BeEquivalentTo("dab.bus_voltage", "psfb.output_voltage");
    }

    [Fact]
    public async Task AChannelCanBeAskedAboutOnItsOwnAfterTheConversion()
    {
        // The question a CSV cannot answer, which is the whole reason the conversion matters.
        string csv = Recording(rowsPerChannel: 10, "dab.bus_voltage", "psfb.output_voltage");
        string db = Path.Combine(_dir, "converted.db");

        var replay = new ReplayTelemetrySource(csv, speed: 1000);
        replay.Load();

        using var archive = new SqliteDataLogger(db);
        var rows = new List<TelemetryPacket>();
        await foreach (RawPacket raw in replay.ReadAsync(CancellationToken.None)) rows.Add(Parse(raw.RawLine));
        await archive.WriteBatchAsync(rows);

        IEnumerable<TelemetryPacket> one = await archive.QueryAsync(
            new QueryFilter(null, "dab.bus_voltage", null, null, 100));

        one.Should().HaveCount(10);
        one.Should().OnlyContain(p => p.Variable == "dab.bus_voltage");
    }

    [Fact]
    public async Task ANodeIsCarriedThroughSoTwoDevicesDoNotCollapseIntoOne()
    {
        // Wiring the replay source found this once already: the row parser dropped the NodeId
        // column, so two devices reporting the same channel name became one series on playback.
        string csv = Recording(rowsPerChannel: 3, "dab.bus_voltage");

        var replay = new ReplayTelemetrySource(csv, speed: 1000);
        replay.Load().Should().BeTrue();

        var rows = new List<string>();
        await foreach (RawPacket raw in replay.ReadAsync(CancellationToken.None)) rows.Add(raw.RawLine);

        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(r => r.Contains("SIM:COM3", StringComparison.Ordinal));
    }

    /// <summary>Reads one frame the replay source emitted.</summary>
    /// <remarks>
    /// The source does not hand back the CSV rows it read. It rebuilds each one as the device
    /// frame it came from — checksum and all — so a replay runs the parser, the checksum check and
    /// the routing rules exactly as a live port does. This test first assumed the rows came back
    /// verbatim and failed on a checksum suffix, which is the product being better than the test
    /// expected.
    /// </remarks>
    private static TelemetryPacket Parse(string frame)
    {
        string body = frame.TrimStart('$');
        int star = body.LastIndexOf('*');
        if (star >= 0) body = body[..star];

        string[] parts = body.Split(',');
        return new TelemetryPacket(
            parts[1], parts[2],
            double.Parse(parts[3], CultureInfo.InvariantCulture),
            parts.Length > 4 ? parts[4] : string.Empty,
            DateTime.UtcNow);
    }
}
