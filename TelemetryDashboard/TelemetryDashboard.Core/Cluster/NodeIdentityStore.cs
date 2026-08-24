using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TelemetryDashboard.Core.Cluster;

/// <summary>
/// Where an installation's identity is kept so that replacing the installation does not lose it.
/// </summary>
/// <remarks>
/// It used to live beside the executable, which is the one directory an update replaces. Nothing in
/// this product performs an update yet, so the defect had never fired — but the identity is the
/// thing ARCHITECTURE.md §2 says must never quietly change, and the first in-place update would
/// have changed it silently: the same rig publishing under a new id, every coverage entry for the
/// old one going to silence, and no error anywhere to explain it.
/// <para>
/// Keyed by the install path rather than by the machine, because per-install is the property that
/// matters and the old location had it by accident. Two hosts run from two directories on one
/// machine are two devices watching two rigs, and one identity between them would interleave their
/// channels — which is the exact collision the identity exists to prevent.
/// </para>
/// <para>
/// What this trades, stated rather than discovered: the id follows the install <em>path</em>, so
/// moving or renaming the directory reads as a new installation, and so does running the same
/// directory as a different user. Both are real; both are what <c>TELEMETRY_HOST_NODE_ID</c> is for,
/// and the start-up banner points at it. The alternative — keying on the directory's contents or on
/// the machine — trades a visible change for an invisible collision, which is the worse of the two.
/// </para>
/// </remarks>
public static partial class NodeIdentityStore
{
    /// <summary>Directory under the user's local application data holding one file per install.</summary>
    public const string DirectoryName = "nodes";

    /// <summary>Product directory the desktop shell already keeps its settings in.</summary>
    public const string ProductDirectoryName = "TelemetryDashboard";

    /// <summary>The file this install's identity is kept in.</summary>
    /// <remarks>
    /// The install path is hashed rather than embedded: a path can be longer than a file name may
    /// be, contains separators, and on a plant machine often contains a customer's name. A digest
    /// is fixed-length, legal everywhere, and discloses nothing to somebody listing the directory.
    /// </remarks>
    /// <param name="root">
    /// Where the product's per-user data lives. Defaults to the platform's local application data,
    /// and is a parameter so a test can be hermetic: a suite that writes into the developer's real
    /// profile leaves state behind that the next run reads, which is how a test starts passing for
    /// a reason that has nothing to do with the code.
    /// </param>
    public static string PathFor(string installDirectory, string? root = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        return Path.Combine(
            root ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductDirectoryName,
            DirectoryName,
            Key(installDirectory) + ".id");
    }

    /// <summary>
    /// Reads this install's identity, migrating one written beside the executable, or creates it.
    /// </summary>
    /// <remarks>
    /// The legacy file wins when both exist, and is copied forward rather than read every time.
    /// An installation that already has an identity must keep it: changing it here would be the
    /// same silent history split this move exists to prevent, arriving on the day of the fix
    /// instead of the day of the update.
    /// </remarks>
    public static NodeIdentity LoadOrCreate(string installDirectory, string? root = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        string durable = PathFor(installDirectory, root);

        string stored = ReadIfValid(durable);
        if (stored.Length > 0) return NodeIdentity.FromStoredId(stored);

        string legacy = ReadIfValid(Path.Combine(installDirectory, NodeIdentity.FileName));
        if (legacy.Length > 0)
        {
            // Migrated, not re-read. Leaving the old file authoritative would mean the identity
            // still lives where an update deletes it.
            //
            // A failed write is not reported as a new identity: the value is right either way, and
            // this run behaves identically. What it costs is that the migration will be attempted
            // again next launch, which is harmless and self-correcting.
            TryWrite(durable, legacy);
            return NodeIdentity.FromStoredId(legacy);
        }

        string generated = Guid.NewGuid().ToString("N");

        // WasCreated stays true whether or not the write succeeded, because both mean the same
        // thing to the caller: this run did not find an identity. A failed write additionally means
        // the next run will not either, and the banner already says so.
        TryWrite(durable, generated);
        return NodeIdentity.FromGeneratedId(generated);
    }

    /// <summary>True when an identity written beside the executable is still the live one.</summary>
    /// <remarks>
    /// For the banner. An operator who has one wants to know it moved, and where to, before an
    /// update rather than after it.
    /// </remarks>
    public static bool HasLegacyFile(string installDirectory) =>
        File.Exists(Path.Combine(installDirectory, NodeIdentity.FileName));

    private static string Key(string installDirectory)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDirectory));

        // Case-folded only where the filesystem is. Doing it everywhere would merge two genuinely
        // different directories on Linux; doing it nowhere would split one directory on Windows
        // into two identities depending on how it was typed.
        if (OperatingSystem.IsWindows()) full = full.ToLowerInvariant();

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..32];
    }
}
