using System.Text;

namespace TelemetryDashboard.Core.Services;

/// <summary>
/// The encoding every file this product writes for another program to read.
/// </summary>
/// <remarks>
/// <see cref="Encoding.UTF8"/> emits a byte-order mark, and .NET's own writers put it at the front
/// of the file. Nothing in UTF-8 needs it and strict readers do not tolerate it: a JSON parser
/// refuses the document outright, and a CSV reader takes the first column's name to be the mark
/// plus the name, so a header of <c>Date</c> matches nothing.
/// <para>
/// Found twice, the same way both times. A backtest equity curve exported so a result could be
/// recomputed elsewhere was refused by a three-line Python <c>csv.DictReader</c> on its first use;
/// an incident report written for a machine to read was refused by <c>json.load</c>. Fixing it
/// privately in one file is what allowed the second, so it is one thing here now.
/// </para>
/// <para>
/// The recorder was writing marks too, which means its own replay reader saw a header that did not
/// begin with <c>Timestamp_ISO</c> and dropped the line as an unparseable row. Recordings already
/// on disk still carry the mark, so readers strip it as well as writers omitting it — a fix that
/// only changes the writer leaves every file written before it unreadable.
/// </para>
/// </remarks>
public static class Utf8Files
{
    /// <summary>UTF-8 with no byte-order mark.</summary>
    public static readonly UTF8Encoding WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The mark itself, for readers that have to take it off a line already read.</summary>
    public const char ByteOrderMark = '﻿';

    /// <summary>Removes a leading byte-order mark, if one is there.</summary>
    /// <remarks>
    /// Applied to the first line rather than to the stream, because that is where it survives:
    /// a reader given the file as text has already decoded it, and the mark arrives as an ordinary
    /// leading character on line one.
    /// </remarks>
    public static string StripMark(string line) =>
        string.IsNullOrEmpty(line) ? line : line.TrimStart(ByteOrderMark);
}
