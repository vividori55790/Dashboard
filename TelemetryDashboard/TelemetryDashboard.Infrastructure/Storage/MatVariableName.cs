using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>
/// What each matrix in a MAT-file is called.
/// </summary>
/// <remarks>
/// Two rules that both decide a name, kept together because they compose: which packets share a
/// matrix, and what that matrix may legally be called once they do. Splitting them across the
/// writer meant the second was easy to find and the first was buried in a LINQ grouping.
/// </remarks>
internal static class MatVariableName
{
    /// <summary>Longest variable name MATLAB accepts.</summary>
    private const int MaxNameLength = 31;

    /// <summary>Names the matrix a packet belongs in.</summary>
    /// <remarks>
    /// The node is prefixed for the whole file or for none of it, decided once from the packets
    /// being written rather than per channel. Naming some matrices one way and some the other
    /// inside a single file would mean a script could not read a name without knowing how many
    /// nodes happened to report that channel.
    /// </remarks>
    public static string For(TelemetryPacket packet, bool qualify)
    {
        string channel = string.IsNullOrWhiteSpace(packet.Variable) ? "channel" : packet.Variable;

        return qualify && !string.IsNullOrWhiteSpace(packet.NodeId)
            ? $"{packet.NodeId}_{channel}"
            : channel;
    }

    /// <summary>MAT variable names must be ASCII identifiers, and distinct within one file.</summary>
    /// <remarks>
    /// The accepted character set is ASCII-only on purpose. <see cref="char.IsLetterOrDigit(char)"/>
    /// accepts any Unicode letter, so a Korean channel name such as "온도" passed sanitisation intact
    /// and was flattened to "??" by <see cref="Encoding.ASCII"/> further down — not a legal MATLAB
    /// identifier, and the name every other non-ASCII channel collapsed onto as well.
    /// <para>
    /// <paramref name="taken"/> is what makes the collapse survivable rather than silent. Two
    /// channels writing the same name produce two matrices called the same thing, and a loader keeps
    /// whichever it read last — the first channel is gone from the export with nothing to show it was
    /// ever there. Truncation to <see cref="MaxNameLength"/> collides the same way.
    /// </para>
    /// </remarks>
    public static string Sanitize(string raw, HashSet<string> taken)
    {
        var builder = new StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
        }

        // MATLAB identifiers must begin with a letter, so a leading digit or underscore is prefixed
        // rather than only a leading digit: sanitising "온도" yields "__", which loads in SciPy but
        // is rejected by isvarname and cannot be typed at a MATLAB prompt.
        string name = builder.ToString();
        if (name.Length == 0 || !char.IsAsciiLetter(name[0])) name = "ch_" + name;
        if (name.Length > MaxNameLength) name = name[..MaxNameLength];

        string unique = name;
        for (int suffix = 2; !taken.Add(unique); suffix++)
        {
            string tail = "_" + suffix.ToString(CultureInfo.InvariantCulture);
            unique = name.Length + tail.Length > MaxNameLength
                ? name[..(MaxNameLength - tail.Length)] + tail
                : name + tail;
        }

        return unique;
    }
}
