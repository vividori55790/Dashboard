using System;
using System.Threading;
using System.Threading.Tasks;

namespace TelemetryDashboard.Host.Startup;

/// <summary>
/// Turns Ctrl-C and process termination into one cancellation token, and holds the process open
/// until the shutdown path says it has finished writing.
/// </summary>
/// <remarks>
/// The blocking wait in the exit handler is the point of this class. On a termination signal the
/// runtime raises <c>ProcessExit</c> and then kills the process on a short timer, so a recorder
/// with a queued tail loses it unless something makes the runtime wait. Returning immediately
/// there would silently truncate every recording that ends the way recordings normally end — by
/// stopping the service.
/// </remarks>
public sealed class ShutdownCoordinator : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource _requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _drained = new(false);
    private int _signalled;

    /// <summary>Hooks Ctrl-C and process exit.</summary>
    public ShutdownCoordinator()
    {
        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    /// <summary>How long the exit handler waits for the drain before giving up.</summary>
    /// <remarks>
    /// Bounded because the runtime's own budget after <c>ProcessExit</c> is finite and a wedged
    /// flush must not turn a stop into a hang.
    /// </remarks>
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Cancelled when shutdown is requested.</summary>
    public CancellationToken Token => _cancellation.Token;

    /// <summary>What asked for the shutdown, once one has been requested.</summary>
    public string? Reason { get; private set; }

    /// <summary>Completes when shutdown is requested.</summary>
    public Task WaitAsync() => _requested.Task;

    /// <summary>Requests shutdown. Repeat calls are ignored, including a second Ctrl-C.</summary>
    public void Request(string reason)
    {
        if (Interlocked.Exchange(ref _signalled, 1) != 0) return;

        Reason = reason;

        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down; the token has served its purpose.
        }

        _requested.TrySetResult();
    }

    /// <summary>Declares the shutdown path finished, releasing a blocked exit handler.</summary>
    public void MarkDrained() => _drained.Set();

    /// <inheritdoc />
    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _cancellation.Dispose();
        _drained.Dispose();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // Cancel the default kill so the flush below gets to run at all.
        e.Cancel = true;
        Request("Ctrl-C");
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        Request("process termination signal");
        _drained.Wait(DrainTimeout);
    }
}
