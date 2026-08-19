using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Infrastructure.Storage;

namespace TelemetryDashboard.Tests;

/// <summary>
/// The pump between the in-memory ring and the durable store, driven against a real SQLite file.
/// </summary>
/// <remarks>
/// The ring evicts its oldest entries once full, so a packet the drain fails to move is gone. Both
/// halves are checked here: that a running loop actually moves packets, and that
/// <see cref="ChannelDataLoggerDrain.StopAsync"/> writes what is left instead of abandoning the tail
/// of a recording at shutdown.
/// <para>
/// Neither test waits for a duration. Progress is observed through a signal the sink raises, and the
/// one test that must know the loop is not touching the ring blocks the loop inside the sink rather
/// than assuming a sleep is long enough.
/// </para>
/// </remarks>
public sealed class ChannelDataLoggerDrainTests : IDisposable
{
    /// <summary>Safety net for a hang, not a synchronisation device. No assertion depends on it.</summary>
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private static void Fill(ChannelDataLogger ring, int from, int count)
    {
        for (int i = from; i < from + count; i++)
        {
            ring.TryEnqueue(new TelemetryPacket("N", "v", i, "u", SqliteDataLoggerTests.At(i)));
        }
    }

    private static async Task<double[]> StoredValues(IDataLogger store) =>
        (await store.QueryAsync(new QueryFilter(Limit: 10_000))).Select(p => p.Value).ToArray();

    [Fact]
    [Trait("Category", "Storage")]
    public async Task StopAsync_WithoutTheLoopEverRunning_WritesEveryBufferedPacket()
    {
        using var store = new SqliteDataLogger(_workspace.File("flush.db"));
        var ring = new ChannelDataLogger(capacity: 100);
        var drain = new ChannelDataLoggerDrain(ring, store, batchSize: 4);
        Fill(ring, 0, 9);

        await drain.StopAsync();

        (await StoredValues(store)).Should().Equal(0, 1, 2, 3, 4, 5, 6, 7, 8);
        drain.DrainedCount.Should().Be(9);
        drain.BatchCount.Should().Be(3, "nine packets at four per transaction is 4 + 4 + 1");
        ring.PendingCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task RunningLoop_MovesBufferedPacketsIntoTheDurableStore()
    {
        using var store = new SqliteDataLogger(_workspace.File("loop.db"));
        var sink = new SignallingSink(store, signalAt: 20);
        var ring = new ChannelDataLogger(capacity: 100);
        await using var drain = new ChannelDataLoggerDrain(ring, sink, batchSize: 8, idleDelay: TimeSpan.FromMilliseconds(1));
        Fill(ring, 0, 20);

        drain.Start();
        await sink.Reached.WaitAsync(HangGuard);
        await drain.StopAsync();

        (await StoredValues(store)).Should().HaveCount(20);
        drain.DrainedCount.Should().Be(20);
        drain.IsRunning.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Storage")]
    public async Task StopAsync_FlushesTheTailTheSleepingLoopHadNotTakenYet()
    {
        using var store = new SqliteDataLogger(_workspace.File("tail.db"));
        var sink = new SignallingSink(store, signalAt: 8, holdFirstWrite: true);
        var ring = new ChannelDataLogger(capacity: 100);
        // A batch larger than the burst means the loop's first pass is not a full batch, so it goes
        // to sleep afterwards; an idle delay far beyond the test means it stays there.
        await using var drain = new ChannelDataLoggerDrain(ring, sink, batchSize: 64, idleDelay: TimeSpan.FromMinutes(5));
        Fill(ring, 0, 8);

        drain.Start();
        // The loop is now parked inside the sink and provably cannot read the ring, so the tail
        // below can only reach the store through the final flush.
        await sink.FirstWriteEntered.WaitAsync(HangGuard);
        Fill(ring, 8, 4);
        sink.ReleaseFirstWrite();

        await drain.StopAsync();

        (await StoredValues(store)).Should().Equal(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
        drain.DrainedCount.Should().Be(12);
        drain.PendingCount.Should().Be(0);
        ring.PendingCount.Should().Be(0);
    }

    /// <summary>Sink that reports progress and can park the drain loop inside its first write.</summary>
    /// <remarks>
    /// Parking is what removes the timing assumption from the tail test: while the loop is held
    /// here it is not in <see cref="ChannelDataLogger.TryRead"/>, so packets enqueued during the
    /// hold cannot be picked up by anything except the final flush.
    /// </remarks>
    private sealed class SignallingSink : IDataLogger
    {
        private readonly IDataLogger _inner;
        private readonly int _signalAt;
        private readonly bool _holdFirstWrite;
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        private int _written;

        internal SignallingSink(IDataLogger inner, int signalAt, bool holdFirstWrite = false)
        {
            _inner = inner;
            _signalAt = signalAt;
            _holdFirstWrite = holdFirstWrite;
        }

        internal Task Reached => _reached.Task;

        internal Task FirstWriteEntered => _entered.Task;

        internal void ReleaseFirstWrite() => _release.TrySetResult();

        public Task WriteAsync(TelemetryPacket packet, CancellationToken cancellationToken = default) =>
            WriteBatchAsync(new[] { packet }, cancellationToken);

        public async Task WriteBatchAsync(IEnumerable<TelemetryPacket> packets, CancellationToken cancellationToken = default)
        {
            var batch = packets.ToList();
            if (_holdFirstWrite && Interlocked.Increment(ref _calls) == 1)
            {
                _entered.TrySetResult();
                await _release.Task.ConfigureAwait(false);
            }

            await _inner.WriteBatchAsync(batch, cancellationToken).ConfigureAwait(false);

            if (Interlocked.Add(ref _written, batch.Count) >= _signalAt) _reached.TrySetResult();
        }

        public Task<IEnumerable<TelemetryPacket>> QueryAsync(QueryFilter filter, CancellationToken cancellationToken = default) =>
            _inner.QueryAsync(filter, cancellationToken);
    }
}
