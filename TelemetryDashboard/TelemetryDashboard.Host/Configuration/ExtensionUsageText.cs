using TelemetryDashboard.Host.Startup;
using TelemetryDashboard.Infrastructure.Plugins;

namespace TelemetryDashboard.Host.Configuration;

/// <summary>
/// The <c>extensions</c> subcommand's help screen.
/// </summary>
/// <remarks>
/// Kept beside <see cref="ExtensionCommandLine"/> so an action cannot be added without the text
/// documenting it sitting one file away — the same arrangement <see cref="UsageText"/> has with
/// <see cref="CommandLineParser"/>.
/// <para>
/// The limits are stated here rather than discovered at the moment of refusal. An operator
/// planning a deployment needs to know before they start that an <c>http</c> catalogue cannot be
/// installed from, not after the package has already been fetched.
/// </para>
/// </remarks>
public static class ExtensionUsageText
{
    /// <summary>Renders the subcommand help.</summary>
    public static string Render() =>
        $"""
        Usage:
          TelemetryDashboard.Host {ExtensionCommandLine.Verb} <action> [arguments]

        Actions:
          list                        What is installed, its version, its state and where it came
                                      from. Reads only.
          install <path>              Install from a local package: either a directory holding
                                      {ExtensionPackageManifestName} and the entry assembly, or a
                                      .dll with a manifest beside it (extension.json, or
                                      <assembly>.extension.json).
          install --catalogue <index> <id>
                                      Install one entry of a JSON catalogue index. The index must
                                      be a file or network-share path: this build will not execute
                                      a payload fetched over http(s), because the hash vouching for
                                      it would come from the same server.
          enable <id>                 Load this extension on the next start.
          disable <id>                Stop loading it, without deleting it.
          remove <id>                 Delete its files and forget it. A host already running is not
                                      blocked by this and keeps the copy it loaded until it exits:
                                      removal changes the next start, not the current one.

        Options:
          --extension-dir <dir>       Store to act on. Default: '{ExtensionDirectoryName}' beside the
                                      executable, which is where the host loads from.

        Before anything is written, an install checks that the manifest parses and names an entry
        assembly, that the assembly's SHA-256 matches the hash the catalogue published, and that it
        loads and exports at least one IPlugin. A package failing any of those is refused with the
        reason, and the store is left untouched.

        """;

    /// <summary>Manifest file name, taken from the parser so the help cannot drift from the code.</summary>
    private const string ExtensionPackageManifestName = ExtensionPackageManifest.FileName;

    /// <summary>Default store folder name, taken from the loader for the same reason.</summary>
    private const string ExtensionDirectoryName = ExtensionLoader.DefaultDirectoryName;
}
