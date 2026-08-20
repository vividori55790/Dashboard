using System;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// The result of verifying or installing one extension: what happened, and in the failure case,
/// precisely why.
/// </summary>
/// <remarks>
/// A boolean would be enough for the caller to branch on and useless to the operator holding the
/// package that was refused. Installing runs a third party's code in this process, so every refusal
/// has to be specific enough to act on — which field of the manifest, which file was missing, which
/// hash was expected against which was found.
/// <para>
/// Refusals are returned, never thrown. A failed install is an ordinary outcome of pointing the
/// host at an untrusted file, and a stack trace out of a command-line verb tells an operator less
/// than one sentence does.
/// </para>
/// </remarks>
public sealed class ExtensionInstallOutcome
{
    private ExtensionInstallOutcome(bool succeeded, string extensionId, string reason, InstalledExtension? installed)
    {
        Succeeded = succeeded;
        ExtensionId = extensionId;
        Reason = reason;
        Installed = installed;
    }

    /// <summary>Whether the extension was accepted.</summary>
    public bool Succeeded { get; }

    /// <summary>Id of the extension, or an empty string when the manifest never yielded one.</summary>
    public string ExtensionId { get; }

    /// <summary>The refusal reason, or a description of what was accepted.</summary>
    public string Reason { get; }

    /// <summary>The record written to the store, or null when nothing was installed.</summary>
    public InstalledExtension? Installed { get; }

    /// <summary>An extension that was verified and stored.</summary>
    public static ExtensionInstallOutcome Accepted(InstalledExtension installed, string reason)
    {
        ArgumentNullException.ThrowIfNull(installed);
        return new ExtensionInstallOutcome(true, installed.Id, reason, installed);
    }

    /// <summary>A refusal, naming the extension when the manifest got far enough to identify one.</summary>
    public static ExtensionInstallOutcome Refused(string reason, string extensionId = "") =>
        new(false, extensionId, reason, null);
}
