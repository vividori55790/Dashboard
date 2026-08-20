using System;
using System.IO;
using System.Text.RegularExpressions;

namespace TelemetryDashboard.Core.Cluster;

/// <summary>
/// Who this installation is, stably, for as long as it exists.
/// </summary>
/// <remarks>
/// A channel called <c>MCU_NODE_1.TEMP</c> is a fine name on one machine and a collision on a
/// hundred. Two hosts each reading a device named <c>MCU_NODE_1</c> publish the same channel for two
/// different physical sensors, and merging them produces a series whose values alternate between
/// two machines — which reads as noisy data rather than as two datasets interleaved. Nothing in the
/// numbers reveals the mistake, so the identity has to prevent it.
///
/// The identifier is generated once and written to disk rather than derived from the machine name.
/// Hostnames are reassigned, duplicated wholesale when a machine image is cloned, and changed by
/// administrators who have no idea anything depends on them. An identifier that quietly changes is
/// worse than no identifier at all: history splits in two and nothing reports an error.
///
/// The machine name is kept alongside, because an operator looking at a fault needs to know which
/// rack to walk to and a GUID will not tell them.
/// </remarks>
public sealed class NodeIdentity
{
    /// <summary>File the identity is persisted in, beside the executable.</summary>
    public const string FileName = "node-identity.txt";

    private NodeIdentity(string id, string machineName, bool wasCreated)
    {
        Id = id;
        MachineName = machineName;
        WasCreated = wasCreated;
    }

    /// <summary>Stable identifier for this installation. Never derived from anything mutable.</summary>
    public string Id { get; }

    /// <summary>Machine name as it was at load time. For humans only; never used for identity.</summary>
    public string MachineName { get; }

    /// <summary>True when this run generated the identity rather than reading an existing one.</summary>
    /// <remarks>
    /// Worth surfacing at start-up. An installation that generates a new identity on every launch
    /// has an unwritable directory, and the symptom of that is history silently splitting into a
    /// new series each restart rather than any error.
    /// </remarks>
    public bool WasCreated { get; }

    /// <summary>What a person should see: the machine name plus enough of the id to disambiguate.</summary>
    public string DisplayName => $"{MachineName} ({Id[..8]})";

    /// <summary>Reads the identity from <paramref name="directory"/>, creating one if absent.</summary>
    /// <remarks>
    /// A malformed file is replaced rather than trusted. The alternative — carrying on with a value
    /// that is not a valid identifier — puts a broken id into every record this node ever emits,
    /// and those records outlive the process that wrote them.
    /// </remarks>
    public static NodeIdentity LoadOrCreate(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        string path = Path.Combine(directory, FileName);

        if (File.Exists(path))
        {
            string existing = ReadTrimmed(path);
            if (IsWellFormed(existing)) return new NodeIdentity(existing, Environment.MachineName, wasCreated: false);
        }

        string generated = Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, generated + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The identity is still usable for this run; it simply will not survive a restart. The
            // caller is told through WasCreated so it can say so rather than letting an operator
            // discover it as a channel history that resets every launch.
        }

        return new NodeIdentity(generated, Environment.MachineName, wasCreated: true);
    }

    /// <summary>Builds an identity from a value the operator supplied, for a managed fleet.</summary>
    /// <remarks>
    /// A deployment that assigns its own names should be able to use them, so long as they are
    /// unambiguous. Anything outside the accepted shape is rejected loudly here rather than being
    /// sanitised into something that might collide with a different node's sanitised name.
    /// </remarks>
    public static NodeIdentity FromAssignedId(string assignedId)
    {
        if (!IsWellFormed(assignedId))
        {
            throw new ArgumentException(
                "A node id must be 4 to 64 characters of letters, digits, hyphen or underscore. "
                + $"'{assignedId}' is not, and quietly rewriting it could collide with another node.",
                nameof(assignedId));
        }

        return new NodeIdentity(assignedId.Trim(), Environment.MachineName, wasCreated: false);
    }

    /// <summary>Qualifies a channel with this node, so the same device name on two hosts stays distinct.</summary>
    public string Qualify(string channel) => $"{Id}/{channel}";

    private static bool IsWellFormed(string? candidate) =>
        candidate is not null && Regex.IsMatch(candidate.Trim(), "^[A-Za-z0-9_-]{4,64}$");

    private static string ReadTrimmed(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
