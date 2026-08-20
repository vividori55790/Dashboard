using System;
using System.Linq;
using TelemetryDashboard.Host.Configuration;
using TelemetryDashboard.Infrastructure.Plugins;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Executes <c>extensions list|install|enable|disable|remove</c> and ends the process.
/// </summary>
/// <remarks>
/// This is the deliberate action <see cref="ExtensionCatalogueReport"/> refuses to be. Listing a
/// catalogue is safe and happens at every start; installing runs a third party's code inside the
/// host, so it needs a person to type it.
/// <para>
/// A refusal exits non-zero with the reason on stderr. A deployment script that pipes an install
/// into a log must be able to fail on it, and an operator must never read "installed" for a package
/// that was rejected.
/// </para>
/// </remarks>
public static class ExtensionCommand
{
    /// <summary>Exit code for a package that was refused, as distinct from a bad command line.</summary>
    public const int ExitRefused = 73;

    /// <summary>Runs the subcommand named in <paramref name="args"/>, whose first word is 'extensions'.</summary>
    public static int Run(string[] args)
    {
        ExtensionCommandLine command = ExtensionCommandLine.Parse(args);
        if (command.Error is not null)
        {
            Console.Error.WriteLine($"telemetry-host extensions: {command.Error}");
            Console.Error.WriteLine(ExtensionUsageText.Render());
            return Program.ExitUsage;
        }

        var store = new ExtensionStore(command.Directory);
        if (store.StateFailure is not null)
        {
            // Reported before anything is attempted: acting on a state file that did not load would
            // write back a list missing every extension it failed to read.
            Console.Error.WriteLine($"telemetry-host extensions: {store.StateFailure}");
            return ExitRefused;
        }

        return command.Action switch
        {
            "list" => List(store),
            "install" => Install(store, command),
            "enable" => SetEnabled(store, command.Target!, true),
            "disable" => SetEnabled(store, command.Target!, false),
            "remove" => Remove(store, command.Target!),
            _ => Program.ExitUsage
        };
    }

    private static int List(ExtensionStore store)
    {
        Console.WriteLine($"extensions in {store.Directory}");

        if (store.Extensions.Count == 0)
        {
            Console.WriteLine("  none installed.");
            Console.WriteLine("  'extensions install <path-to-dll-or-package-dir>' adds one.");
            return 0;
        }

        foreach (InstalledExtension e in store.Extensions)
        {
            Console.WriteLine($"  {e.Id,-24}{e.Name,-30}{e.Version,-8}{e.State}");
            Console.WriteLine($"  {string.Empty,-24}sha256 {Short(e.Sha256)}  entry {e.EntryAssembly}");
            Console.WriteLine($"  {string.Empty,-24}installed {e.InstalledUtc:u} from {e.Origin}");
        }

        int enabled = store.Extensions.Count(e => e.Enabled);
        Console.WriteLine($"  {store.Extensions.Count} installed -- {enabled} enabled, "
            + $"{store.Extensions.Count - enabled} disabled.");
        return 0;
    }

    private static int Install(ExtensionStore store, ExtensionCommandLine command)
    {
        var installer = new ExtensionInstaller(store);
        ExtensionInstallOutcome outcome;

        if (command.Catalogue is not null)
        {
            outcome = ExtensionCatalogueSource.TryResolve(
                command.Catalogue, command.Target!,
                out ExtensionInstallSource? source, out ExtensionInstallOutcome? refusal)
                ? installer.Install(source!)
                : refusal!;
        }
        else
        {
            outcome = installer.InstallFromPath(command.Target!);
        }

        if (!outcome.Succeeded)
        {
            Console.Error.WriteLine($"REFUSED: {outcome.Reason}");
            Console.Error.WriteLine("Nothing was written to the extension store.");
            return ExitRefused;
        }

        Console.WriteLine($"INSTALLED {outcome.ExtensionId} {outcome.Installed!.Version}");
        Console.WriteLine($"  {outcome.Reason}");
        Console.WriteLine($"  state: {outcome.Installed.State} -- it will load on the next host start.");
        return 0;
    }

    private static int SetEnabled(ExtensionStore store, string id, bool enabled)
    {
        if (!store.SetEnabled(id, enabled))
        {
            Console.Error.WriteLine($"REFUSED: no extension with id '{id}' is installed.");
            return ExitRefused;
        }

        Console.WriteLine($"{(enabled ? "ENABLED" : "DISABLED")} {id}");
        Console.WriteLine($"  recorded in {store.Directory} -- the choice survives a restart.");
        return 0;
    }

    private static int Remove(ExtensionStore store, string id)
    {
        if (!store.Remove(id, out string failure))
        {
            Console.Error.WriteLine($"REFUSED: {failure}");
            return ExitRefused;
        }

        Console.WriteLine($"REMOVED {id}");
        Console.WriteLine($"  files deleted from {store.DirectoryFor(id)}");
        Console.WriteLine("  a host already running keeps the copy it loaded until it exits.");
        return 0;
    }

    /// <summary>First 16 hex characters, enough to compare by eye without wrapping the line.</summary>
    private static string Short(string sha) =>
        string.IsNullOrEmpty(sha) ? "(none recorded)" : sha[..Math.Min(16, sha.Length)] + "...";
}
