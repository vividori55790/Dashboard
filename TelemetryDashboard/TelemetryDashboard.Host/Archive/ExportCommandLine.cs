using System;
using System.Globalization;
using System.IO;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Host.Configuration;

namespace TelemetryDashboard.Host.Archive;

/// <summary>
/// The <c>export</c> subcommand: which archive, which window, and where the file goes.
/// </summary>
/// <remarks>
/// A subcommand because it ends, like <c>backtest</c> and <c>extensions</c>: it reads a file,
/// writes a file and exits, and binds no socket.
/// <para>
/// It exists because the two halves of this product had drifted apart. The headless host is what
/// actually sits on a bench for eight hours filling an archive; the desktop shell is what had the
/// only button that could get that archive back out as a MAT-file. An operator whose recording is
/// on a Linux box, or on a machine with no display, had no way to ask for their own data.
/// </para>
/// </remarks>
public sealed class ExportCommandLine
{
    /// <summary>The word that selects this subcommand.</summary>
    public const string Verb = "export";

    /// <summary>Archive database to read.</summary>
    public string ArchivePath { get; private init; } = string.Empty;

    /// <summary>File to write.</summary>
    public string OutputPath { get; private init; } = string.Empty;

    /// <summary>Window and channel selection, as the stores understand it.</summary>
    public QueryFilter Filter { get; private init; } = Everything;

    /// <summary>Whether help was asked for rather than an export.</summary>
    public bool ShowHelp { get; private init; }

    /// <summary>Why the command line was rejected, or null.</summary>
    public string? Error { get; private init; }

    /// <summary>
    /// Every packet in the archive.
    /// </summary>
    /// <remarks>
    /// <see cref="QueryFilter"/> defaults <c>Limit</c> to 1000 and both stores order oldest-first,
    /// so accepting that default would hand somebody exporting an eight-hour recording the first
    /// sixteen minutes of it under a message saying the export succeeded. An export says everything
    /// unless it was asked for less.
    /// </remarks>
    public static QueryFilter Everything => new(Limit: int.MaxValue);

    /// <summary>Whether <paramref name="args"/> selects this subcommand at all.</summary>
    public static bool Matches(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Verb, StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses <c>export &lt;archive.db&gt; --out &lt;file&gt; [options]</c>.</summary>
    public static ExportCommandLine Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? archive = null, output = null, node = null, channel = null;
        DateTime? from = null, to = null;
        int limit = int.MaxValue;

        for (int i = 1; i < args.Length; i++)
        {
            string flag = args[i];
            switch (flag)
            {
                case "--help" or "-h":
                    return new ExportCommandLine { ShowHelp = true };
                case "--out" or "-o":
                    if (!ArgumentCursor.TryValue(args, ref i, out output)) return Missing(flag);
                    break;
                case "--node":
                    if (!ArgumentCursor.TryValue(args, ref i, out node)) return Missing(flag);
                    break;
                case "--channel" or "--variable":
                    if (!ArgumentCursor.TryValue(args, ref i, out channel)) return Missing(flag);
                    break;
                case "--from":
                    if (!ArgumentCursor.TryValue(args, ref i, out string rawFrom)) return Missing(flag);
                    if (!TryTimestamp(rawFrom, out DateTime start)) return Refuse(TimeRefusal(flag, rawFrom));
                    from = start;
                    break;
                case "--to":
                    if (!ArgumentCursor.TryValue(args, ref i, out string rawTo)) return Missing(flag);
                    if (!TryTimestamp(rawTo, out DateTime end)) return Refuse(TimeRefusal(flag, rawTo));
                    to = end;
                    break;
                case "--limit":
                    if (!ArgumentCursor.TryValue(args, ref i, out string rawLimit)) return Missing(flag);
                    if (!int.TryParse(rawLimit, NumberStyles.Integer, CultureInfo.InvariantCulture, out limit)
                        || limit <= 0)
                    {
                        return Refuse($"--limit needs a positive whole number, not '{rawLimit}'.");
                    }
                    break;
                default:
                    if (flag.StartsWith('-')) return Refuse($"unknown argument '{flag}'.");
                    if (archive is not null) return Refuse($"only one archive can be exported, and '{archive}' was already named.");
                    archive = flag;
                    break;
            }
        }

        if (archive is null) return Refuse("an archive to export is required.");
        if (output is null) return Refuse("--out is required: an export has to be told where to write.");
        if (from is { } a && to is { } b && a > b) return Refuse("--from is after --to, so the window is empty.");

        return new ExportCommandLine
        {
            ArchivePath = Path.GetFullPath(archive),
            OutputPath = Path.GetFullPath(output),
            Filter = new QueryFilter(node, channel, from, to, limit)
        };
    }

    /// <summary>Reads an ISO-8601 stamp, and refuses one that leaves the day open.</summary>
    /// <remarks>
    /// A missing zone is assumed to be UTC, which is what every stamp this product writes carries
    /// anyway. A missing <em>date</em> is refused: <c>DateTime.TryParse</c> happily reads "14:00"
    /// and fills in today, so an operator asking for two o'clock on a recording made yesterday
    /// would get a window over a day the archive never covered, and an export that succeeded with
    /// nothing in it or -- worse, on a rig still running -- with the wrong afternoon.
    /// <para>
    /// <c>/api/history</c> still fills in today for the same input. Refusing there means returning
    /// no constraint at all, because its reader answers with a nullable and null already means
    /// "unbounded" -- so the same guess would silently widen the window instead of narrowing it to
    /// the wrong day, and fixing it properly is a refusal path that endpoint does not yet have.
    /// </para>
    /// </remarks>
    private static bool TryTimestamp(string raw, out DateTime parsed) =>
        DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal
            | DateTimeStyles.NoCurrentDateDefault, out parsed)
        && parsed.Year > 1;

    private static string TimeRefusal(string flag, string raw) =>
        $"{flag} needs an ISO-8601 timestamp carrying a date, such as 2026-08-21T14:00:00Z, "
        + $"not '{raw}'.";

    private static ExportCommandLine Missing(string flag) => Refuse($"{flag} requires a value.");

    private static ExportCommandLine Refuse(string message) => new() { Error = message };
}
