using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TelemetryDashboard.Core.Storage;

namespace TelemetryDashboard.Infrastructure.Storage;

/// <summary>The prune path, which is the only way anything is ever deleted from this store.</summary>
/// <remarks>
/// Nothing calls this on a timer and nothing calls it at start-up. Retention runs when an operator
/// or a host asks for it, against a policy that must be enabled before a single row is destroyed —
/// so the behaviour on a first run, and on every run of a store nobody configured, is that all data
/// is kept.
/// </remarks>
public sealed partial class TieredTelemetryStore
{
    /// <summary>Raised after every prune, armed or not, carrying what was or would have been removed.</summary>
    /// <remarks>
    /// The report also goes into the database's own <c>retention_log</c> table. The event exists so
    /// a host can put it in front of a person as well, since a deletion nobody was told about is
    /// one nobody can question.
    /// </remarks>
    public event EventHandler<RetentionPruneReport>? Pruned;

    /// <summary>
    /// Applies <see cref="Retention"/>, or reports what applying it would do when it is disabled.
    /// </summary>
    /// <param name="nowUtc">
    /// Instant the cutoffs are measured back from. Defaults to <see cref="DateTime.UtcNow"/>;
    /// passing it explicitly is what makes retention testable without waiting days.
    /// </param>
    /// <param name="cancellationToken">Cancels before the transaction commits.</param>
    /// <returns>
    /// What was removed, with <see cref="RetentionPruneReport.Applied"/> false when the policy is
    /// disabled and the run was therefore a dry run.
    /// </returns>
    /// <remarks>
    /// Serialised against writes on the same gate: a prune deleting blocks while a batch commits
    /// would otherwise meet SQLite's single-writer rule as a lock timeout that looks like a fault.
    /// </remarks>
    public async Task<RetentionPruneReport> PruneAsync(
        DateTime? nowUtc = null, CancellationToken cancellationToken = default)
    {
        DateTime instant = nowUtc ?? DateTime.UtcNow;

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        RetentionPruneReport report;
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            report = TieredRetentionPruner.Prune(connection, Retention, instant);
        }
        finally
        {
            _writeLock.Release();
        }

        Pruned?.Invoke(this, report);
        return report;
    }
}
