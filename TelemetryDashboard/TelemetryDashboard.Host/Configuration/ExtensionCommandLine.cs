using System;
using System.IO;
using TelemetryDashboard.Host.Startup;

namespace TelemetryDashboard.Host.Configuration;

/// <summary>
/// The <c>extensions</c> subcommand: which action was asked for, against which store.
/// </summary>
/// <remarks>
/// A subcommand rather than more flags on the server command line, because these actions do not
/// serve telemetry — they end. A host that both bound a socket and installed an extension in one
/// invocation would leave an operator guessing whether the install happened before or after the
/// process started running a stranger's code.
/// <para>
/// Parsed separately from <see cref="CommandLineParser"/> for the same reason. Mixing them would
/// mean every server flag had to be accepted, and silently ignored, by every extension action.
/// </para>
/// </remarks>
public sealed class ExtensionCommandLine
{
    /// <summary>The word that selects this subcommand.</summary>
    public const string Verb = "extensions";

    private ExtensionCommandLine(string action, string? target, string? catalogue, string directory, string? error)
    {
        Action = action;
        Target = target;
        Catalogue = catalogue;
        Directory = directory;
        Error = error;
    }

    /// <summary>One of <c>list</c>, <c>install</c>, <c>enable</c>, <c>disable</c>, <c>remove</c>.</summary>
    public string Action { get; }

    /// <summary>A path for <c>install</c>, an extension id for the rest, or null for <c>list</c>.</summary>
    public string? Target { get; }

    /// <summary>Catalogue index for <c>install --catalogue</c>, or null for a local install.</summary>
    public string? Catalogue { get; }

    /// <summary>Store directory the action applies to.</summary>
    public string Directory { get; }

    /// <summary>Why the command line was rejected, or null.</summary>
    public string? Error { get; }

    /// <summary>Whether <paramref name="args"/> selects this subcommand at all.</summary>
    public static bool Matches(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Verb, StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses <c>extensions &lt;action&gt; [target] [--catalogue idx] [--extension-dir d]</c>.</summary>
    public static ExtensionCommandLine Parse(string[] args)
    {
        string directory = DefaultDirectory();
        if (args.Length < 2) return Fail("an action is required: list, install, enable, disable, remove.", directory);

        string action = args[1].ToLowerInvariant();
        string? target = null;
        string? catalogue = null;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--extension-dir":
                    if (++i >= args.Length) return Fail("--extension-dir requires a value.", directory);
                    directory = Path.GetFullPath(args[i]);
                    break;

                case "--catalogue" or "--catalog":
                    if (++i >= args.Length) return Fail("--catalogue requires a value.", directory);
                    catalogue = args[i];
                    break;

                default:
                    if (args[i].StartsWith('-')) return Fail($"unknown argument '{args[i]}'.", directory);
                    if (target is not null) return Fail($"'{args[i]}' is a second target; only one is accepted.", directory);
                    target = args[i];
                    break;
            }
        }

        return Validate(action, target, catalogue, directory);
    }

    /// <summary>Rejects an action that cannot do anything with what it was given.</summary>
    /// <remarks>
    /// Checked here rather than at execution so a mistyped command costs nothing. An
    /// <c>install</c> with no path that reached the installer would have to invent a default, and
    /// the only honest default is a refusal.
    /// </remarks>
    private static ExtensionCommandLine Validate(string action, string? target, string? catalogue, string directory)
    {
        switch (action)
        {
            case "list":
                return new ExtensionCommandLine(action, null, null, directory, null);

            case "install":
                if (catalogue is not null && target is null) return Fail("install --catalogue <index> needs the id to install.", directory);
                if (target is null) return Fail("install needs a path to a .dll or a package directory.", directory);
                return new ExtensionCommandLine(action, target, catalogue, directory, null);

            case "enable" or "disable" or "remove":
                if (target is null) return Fail($"{action} needs an extension id.", directory);
                return new ExtensionCommandLine(action, target, null, directory, null);

            default:
                return Fail($"unknown action '{action}'. Expected list, install, enable, disable or remove.", directory);
        }
    }

    /// <summary>Store beside the executable, which is also where the running host looks.</summary>
    public static string DefaultDirectory() =>
        Path.Combine(AppContext.BaseDirectory, ExtensionLoader.DefaultDirectoryName);

    private static ExtensionCommandLine Fail(string message, string directory) =>
        new(string.Empty, null, null, directory, message);
}
