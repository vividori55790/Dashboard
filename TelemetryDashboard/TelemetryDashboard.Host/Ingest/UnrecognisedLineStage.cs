using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelemetryDashboard.Core.Records;

namespace TelemetryDashboard.Host.Ingest;

/// <summary>One shape of unparsed line: how it started, how often, and one real example.</summary>
public sealed record UnrecognisedShape(string Prefix, long Count, string Example);

/// <summary>
/// Accounts for lines that arrived on a port and that nothing knew how to read.
/// </summary>
/// <remarks>
/// Before this stage existed those lines were dropped where the router returned no packets and the
/// positional parser rejected the line — no counter, no message, nothing. The two failures an
/// operator most wants to tell apart, "my device is not transmitting" and "my device is
/// transmitting something this host does not understand", produced the identical symptom: an empty
/// chart. Counting them and keeping one verbatim example per shape turns the second into a
/// question with an answer.
///
/// Bounded on purpose. A device emitting a distinct line every millisecond must not be able to
/// grow this without limit, so distinct prefixes are capped and the rest are tallied together.
/// </remarks>
public sealed class UnrecognisedLineStage : IRecordStage
{
    /// <summary>Distinct prefixes retained before the remainder is merged into one bucket.</summary>
    public const int MaxTrackedShapes = 16;

    private const int PrefixLength = 16;
    private const int ExampleLength = 120;

    private readonly Dictionary<string, (long Count, string Example)> _shapes = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private long _total;
    private long _untracked;

    public string Name => "unrecognised-lines";

    /// <summary>Every line that reached this stage.</summary>
    public long Total { get { lock (_gate) return _total; } }

    /// <summary>Lines whose prefix arrived after the tracking table was full.</summary>
    public long UntrackedShapeCount { get { lock (_gate) return _untracked; } }

    /// <summary>Text is exactly what an unparsed line is; anything else belongs to another stage.</summary>
    public bool CanHandle(DataValue value) => value.Kind == DataValueKind.Text;

    public ValueTask ProcessAsync(DataRecord record, CancellationToken cancellationToken = default)
    {
        if (record.Value is not DataValue.Text text) return ValueTask.CompletedTask;

        string line = text.Value ?? string.Empty;
        string prefix = ShapeOf(line);

        lock (_gate)
        {
            _total++;

            if (_shapes.TryGetValue(prefix, out (long Count, string Example) seen))
            {
                _shapes[prefix] = (seen.Count + 1, seen.Example);
            }
            else if (_shapes.Count < MaxTrackedShapes)
            {
                _shapes[prefix] = (1, Truncate(line, ExampleLength));
            }
            else
            {
                _untracked++;
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>What was seen, most frequent first.</summary>
    public IReadOnlyList<UnrecognisedShape> Shapes()
    {
        lock (_gate)
        {
            return _shapes
                .Select(kv => new UnrecognisedShape(kv.Key, kv.Value.Count, kv.Value.Example))
                .OrderByDescending(s => s.Count)
                .ThenBy(s => s.Prefix, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>
    /// The leading run of characters before the first delimiter, which is what identifies a frame
    /// format in every protocol this host has met — and harmless when it identifies nothing.
    /// </summary>
    public static string ShapeOf(string line)
    {
        string trimmed = (line ?? string.Empty).Trim();
        if (trimmed.Length == 0) return "(blank)";

        int end = trimmed.IndexOfAny(new[] { ',', ' ', '\t', ':', ';', '=' });
        string prefix = end > 0 ? trimmed[..end] : trimmed;

        return Truncate(prefix, PrefixLength);
    }

    private static string Truncate(string value, int limit) =>
        value.Length <= limit ? value : value[..limit] + "\u2026";
}
