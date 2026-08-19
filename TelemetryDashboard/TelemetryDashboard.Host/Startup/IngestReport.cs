using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryDashboard.Core.Records;
using TelemetryDashboard.Host.Ingest;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// What the ingest path actually did, printed when the host drains.
/// </summary>
/// <remarks>
/// A sample count on its own answers the wrong question. An operator whose console stayed empty
/// needs to know whether nothing arrived, or whether plenty arrived in a format this host could not
/// read, or whether a channel flooded and was cut off — three different problems that used to
/// produce one identical line. Everything printed here is a counter the run kept, never an
/// inference: when a number is zero it is because nothing happened, not because nothing was
/// measured.
/// </remarks>
public static class IngestReport
{
    /// <summary>Longest example line echoed back, so one bad device cannot flood the report.</summary>
    private const int MaxShapesShown = 5;

    /// <summary>Prints the ingest summary. Silent when there was no ingest at all.</summary>
    public static void Print(TelemetryIngestPump? pump, ITelemetrySource? source)
    {
        if (pump is null) return;

        Console.WriteLine($"[shutdown] ingest closed after {pump.SamplesPublished} samples.");

        foreach (string line in Render(pump, source))
        {
            Console.WriteLine(line);
        }
    }

    /// <summary>Builds the report lines, so their wording can be asserted without a console.</summary>
    public static IReadOnlyList<string> Render(TelemetryIngestPump pump, ITelemetrySource? source)
    {
        ArgumentNullException.ThrowIfNull(pump);
        var lines = new List<string>();

        foreach (StageActivity stage in pump.Records.Activity())
        {
            lines.Add($"           {stage.Stage,-20} {stage.Accepted} handled, {stage.Declined} declined"
                      + (stage.Faulted > 0 ? $", {stage.Faulted} faulted" : string.Empty));
        }

        long unreadable = pump.Records.UnreadableSamples;
        if (unreadable > 0)
        {
            lines.Add($"           {unreadable} numeric samples had no reading and were not stored.");
        }

        lines.AddRange(UnparsedLines(pump));

        string? guard = pump.Guard.Summary();
        if (guard is not null) lines.Add("           " + guard);

        if (source is SerialTelemetrySource serial && serial.FaultCount > 0)
        {
            lines.Add($"           serial link dropped {serial.FaultCount} time(s), recovered {serial.RecoveryCount}.");
        }

        return lines;
    }

    private static IEnumerable<string> UnparsedLines(TelemetryIngestPump pump)
    {
        UnrecognisedLineStage unrecognised = pump.Records.Unrecognised;
        if (unrecognised.Total == 0) yield break;

        yield return $"           {unrecognised.Total} lines arrived that no routing rule or parser could read.";
        yield return "           They are not a device fault; they are a device this host is not configured for.";

        IReadOnlyList<UnrecognisedShape> shapes = unrecognised.Shapes();
        foreach (UnrecognisedShape shape in shapes.Take(MaxShapesShown))
        {
            yield return $"             {shape.Count,8}x  {shape.Prefix,-18}  e.g. {shape.Example}";
        }

        if (shapes.Count > MaxShapesShown)
        {
            yield return $"             ... and {shapes.Count - MaxShapesShown} further shapes.";
        }

        if (unrecognised.UntrackedShapeCount > 0)
        {
            yield return $"             {unrecognised.UntrackedShapeCount} more lines had shapes beyond the "
                         + $"{UnrecognisedLineStage.MaxTrackedShapes} tracked; counted, not sampled.";
        }
    }
}
