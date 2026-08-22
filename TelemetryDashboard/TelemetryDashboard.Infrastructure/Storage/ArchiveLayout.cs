using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>Which layout a telemetry archive file on disk actually holds.</summary>
public enum ArchiveLayoutKind
{
    /// <summary>A SQLite database with neither archive's tables in it.</summary>
    Unknown,

    /// <summary>One row per sample, written by <see cref="SqliteDataLogger"/>.</summary>
    Rows,

    /// <summary>Compressed blocks and rollups, written by <see cref="TieredTelemetryStore"/>.</summary>
    Tiered,

    /// <summary>Both layouts' tables are present, so neither can be assumed to be the archive.</summary>
    Ambiguous
}

/// <summary>
/// Reads an archive file to find out which store wrote it, without writing to it.
/// </summary>
/// <remarks>
/// A <c>.db</c> produced by this product may be either layout and the file name says nothing about
/// which. That is fine while the process that opens it is the one that made it, and stops being
/// fine the moment anything reads an archive it did not create — a later run, another machine, or
/// the export subcommand.
/// <para>
/// Guessing is not an option here, and not because a wrong guess reads badly: both stores open with
/// <c>CREATE TABLE IF NOT EXISTS</c>, so opening a row archive as a tiered one <em>adds tiered
/// tables to the operator's file</em>. The detection has to happen before the open, which is why it
/// is here as its own read rather than a property of either store.
/// </para>
/// <para>
/// The tables are disjoint by construction — <c>telemetry_log</c> against <c>raw_block</c> — so
/// finding both is not a layout, it is evidence that some earlier run already did the opening this
/// class exists to prevent. It is reported as <see cref="ArchiveLayoutKind.Ambiguous"/> rather than
/// resolved, because either half may hold the recording somebody is asking for.
/// </para>
/// </remarks>
public static class ArchiveLayout
{
    /// <summary>Table that only the row store creates.</summary>
    public const string RowTable = "telemetry_log";

    /// <summary>Table that only the tiered store creates.</summary>
    public const string TieredTable = "raw_block";

    /// <summary>
    /// Reads which layout <paramref name="databasePath"/> holds.
    /// </summary>
    /// <remarks>
    /// A file that does not exist is a caller's error rather than a layout, because SQLite would
    /// create it and this would then truthfully report an empty database that the caller had just
    /// brought into being by asking about it.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="databasePath"/> is blank.</exception>
    /// <exception cref="SqliteException">The file is not a readable SQLite database.</exception>
    public static ArchiveLayoutKind Detect(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path must be provided.", nameof(databasePath));
        }

        var found = new HashSet<string>(StringComparer.Ordinal);

        using var connection = new SqliteConnection(
            SqliteTelemetrySchema.BuildConnectionString(databasePath));
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"SELECT name FROM sqlite_master WHERE type = 'table' AND name IN ('{RowTable}', '{TieredTable}');";

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) found.Add(reader.GetString(0));

        return (found.Contains(RowTable), found.Contains(TieredTable)) switch
        {
            (true, true) => ArchiveLayoutKind.Ambiguous,
            (true, false) => ArchiveLayoutKind.Rows,
            (false, true) => ArchiveLayoutKind.Tiered,
            _ => ArchiveLayoutKind.Unknown
        };
    }
}
