using System;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// One extension the host has accepted onto disk, and whether the operator wants it running.
/// </summary>
/// <remarks>
/// Persisted to <c>installed.json</c> as-is, which is why every property has a setter and a
/// defaulted value: a state file written by an older build must still deserialise into a usable
/// record rather than throwing and taking every other extension's state with it.
/// <para>
/// <see cref="Enabled"/> is stored rather than inferred from the files present, so disabling an
/// extension does not mean deleting it. An operator narrowing down which extension is misbehaving
/// needs to turn one off and back on without re-fetching it, and needs that decision to survive the
/// restart they are about to perform.
/// </para>
/// <para>
/// <see cref="LoadFailure"/> is deliberately not persisted. It describes what happened in the
/// current process; writing it to disk would let yesterday's failure be reported as today's state
/// long after the cause was fixed.
/// </para>
/// </remarks>
public sealed class InstalledExtension
{
    /// <summary>Catalogue id, and the name of the directory holding the extension.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name from the manifest.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Version from the manifest.</summary>
    public string Version { get; set; } = "0.0.0";

    /// <summary>File name of the assembly to load, inside this extension's directory.</summary>
    public string EntryAssembly { get; set; } = string.Empty;

    /// <summary>Lowest host API version the manifest claims to work against.</summary>
    public string MinApiVersion { get; set; } = "1.0.0";

    /// <summary>SHA-256 of the installed assembly, computed at install time from the bytes stored.</summary>
    /// <remarks>
    /// Recorded even when the manifest published none, so a later integrity check compares against
    /// what was actually accepted rather than against what a manifest now claims.
    /// </remarks>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Where the extension was installed from, for an operator retracing a deployment.</summary>
    public string Origin { get; set; } = string.Empty;

    /// <summary>When it was installed, UTC.</summary>
    public DateTime InstalledUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Whether the host should load it on the next start.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Why this extension did not load in the current process, or null.</summary>
    /// <remarks>
    /// Runtime only — see the type remarks. Set by the loader so the start-up report can name a
    /// failed extension instead of leaving a gap between "installed" and "running" that nothing
    /// explains.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? LoadFailure { get; set; }

    /// <summary>One-word state for the report: what this extension is doing right now.</summary>
    /// <remarks>
    /// A failed extension is never reported as merely disabled. The two look the same from outside
    /// — nothing is running — and lead an operator to opposite actions.
    /// <para>
    /// Not persisted, for the same reason <see cref="LoadFailure"/> is not: written to the state
    /// file it would record this run's outcome as though it were a stored property, and a later
    /// build reading it back would report a stale verdict.
    /// </para>
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public string State => LoadFailure is not null ? "failed" : Enabled ? "enabled" : "disabled";
}
