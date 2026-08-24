using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TelemetryDashboard.Host.Configuration;

/// <summary>
/// The <c>sniff</c> subcommand: listen to a device for a while and write down what it says.
/// </summary>
/// <remarks>
/// Configuring a real MCU used to start from the answer. <c>--rules</c> renames what the device
/// sends into what the profile declares, and writing that file required already knowing which names
/// the firmware uses, in which units, on which frame tag — none of which is written down anywhere
/// except in the device.
/// <para>
/// So this listens first. It is a subcommand rather than a flag because it ends: it opens the
/// source, reads for a fixed time, writes a file and exits, and binds no socket. An operator runs
/// it once, opens the file it wrote, and fills in the blanks.
/// </para>
/// <para>
/// The source flags are not re-parsed here. They are handed to <see cref="CommandLineParser"/>
/// exactly as the serving host would receive them, because the entire value of this command rests
/// on it hearing what the real run will hear — a second parser that understood <c>--serial</c> even
/// slightly differently would draft a file for a stream nobody is going to have.
/// </para>
/// </remarks>
public sealed partial class SniffCommandLine
{
    /// <summary>The word that selects this subcommand.</summary>
    public const string Verb = "sniff";

    /// <summary>How long to listen. Long enough to hear every channel at least once.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(15);

    private SniffCommandLine(
        HostOptions source, TimeSpan duration, string output, bool force, bool verify, bool showHelp,
        string? error, string invocation)
    {
        Source = source;
        Duration = duration;
        OutputPath = output;
        Force = force;
        Verify = verify;
        ShowHelp = showHelp;
        Error = error;
        Invocation = invocation;
    }

    /// <summary>Where the readings come from, parsed exactly as the serving host parses it.</summary>
    public HostOptions Source { get; }

    public TimeSpan Duration { get; }

    /// <summary>File to write the drafted rules to.</summary>
    public string OutputPath { get; }

    /// <summary>Whether an existing output file may be replaced.</summary>
    public bool Force { get; }

    /// <summary>
    /// Check the rules in force instead of drafting new ones. Writes nothing.
    /// </summary>
    /// <remarks>
    /// The command's help already suggested --rules for exactly this, and following that advice
    /// produced a rules.json nobody asked for -- or a refusal, when one was already there. A check
    /// with a side effect is one people stop running.
    /// </remarks>
    public bool Verify { get; }

    public bool ShowHelp { get; }

    /// <summary>Why the command line was rejected, or null.</summary>
    public string? Error { get; }

    /// <summary>The command as typed, recorded in the drafted file's header.</summary>
    public string Invocation { get; }

    /// <summary>Whether <paramref name="args"/> selects this subcommand at all.</summary>
    public static bool Matches(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Verb, StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses <c>sniff [source flags] [--for 15s] [--out rules.json] [--force] [--verify]</c>.</summary>
    public static SniffCommandLine Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        TimeSpan duration = DefaultDuration;
        string output = "rules.json";
        bool force = false, verify = false, help = false;
        var rest = new List<string>();

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--for":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? window))
                    {
                        return Fail("--for needs a duration, e.g. --for 15s.", args);
                    }
                    if (!TryDuration(window, out duration)) return Fail($"--for {window}: {DurationHelp}", args);
                    break;

                case "--out":
                    if (!ArgumentCursor.TryValue(args, ref i, out string? path))
                    {
                        return Fail("--out needs a file to write, e.g. --out rules.json.", args);
                    }
                    output = path;
                    break;

                case "--force":
                    force = true;
                    break;

                case "--verify":
                    verify = true;
                    break;

                case "--help" or "-h" or "-?" or "/?":
                    help = true;
                    break;

                default:
                    // Everything else is the serving host's vocabulary, and stays that way.
                    rest.Add(args[i]);
                    break;
            }
        }

        HostOptions source = CommandLineParser.Parse([.. rest], EnvironmentVariables.Read());

        return new SniffCommandLine(
            source, duration, output, force, verify, help, source.Error,
            Verb + " " + string.Join(' ', args[1..]));
    }

    private const string DurationHelp =
        "a duration is a number and an optional unit, e.g. 20, 30s or 2m.";

    private static SniffCommandLine Fail(string message, string[] args) =>
        new(new HostOptions(), DefaultDuration, "rules.json", force: false, verify: false,
            showHelp: false, message, Verb + " " + string.Join(' ', args[1..]));
}
