using System;
using System.IO;

namespace TelemetryDashboard.Core.Security;

/// <summary>
/// Where a screen-lock password survives a restart.
/// </summary>
/// <remarks>
/// It did not, before. The hash lived in a private field on a service constructed with the main
/// window, so a password set in one session was gone in the next — which meant the only credential
/// that ever actually applied was the literal compiled into the check. Persistence is not a
/// refinement here; without it, setting a password has no meaning.
/// <para>
/// Plain file, not the registry and not DPAPI. What is written is already a salted PBKDF2
/// derivation, so the file discloses nothing that needs protecting in turn, and a plain file is
/// readable on every platform this product runs on — the headless host has no registry and no
/// DPAPI, and a store only the desktop shell can read would be a store the product cannot share.
/// </para>
/// </remarks>
public static class CredentialFile
{
    /// <summary>Reads a credential, or null when there is none to read.</summary>
    /// <remarks>
    /// An unreadable or malformed file reads as "no password configured" rather than as an error,
    /// and that is the safe direction here: the lock screen then asks the operator to set one
    /// instead of refusing every password with no way forward. A corrupt file cannot leave the
    /// machine permanently locked, and it cannot open it either — enrollment is only reachable
    /// from a session that is already unlocked.
    /// </remarks>
    public static PasswordCredential? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            return PasswordCredential.TryParse(File.ReadAllText(path), out PasswordCredential c)
                ? c
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes a credential, creating the directory if it is not there.</summary>
    /// <returns>Null on success, or why it could not be written.</returns>
    public static string? Save(string path, PasswordCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (string.IsNullOrWhiteSpace(path)) return "No path was given for the credential file.";

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Written to a temporary file and moved into place. A half-written credential is a
            // machine nobody can unlock, and a process killed mid-write is not a rare event on a
            // plant floor.
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, credential.ToStorage());
            File.Move(temporary, path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    /// <summary>The per-user location the desktop shell uses.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TelemetryDashboard", "screenlock.cred");
}
