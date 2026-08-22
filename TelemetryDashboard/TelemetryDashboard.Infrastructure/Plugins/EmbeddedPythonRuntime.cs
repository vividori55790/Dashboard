using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronPython.Hosting;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>Outcome of running one Python snippet.</summary>
public readonly record struct PythonRunResult(bool Succeeded, string Error)
{
    public static PythonRunResult Ok() => new(true, string.Empty);
    public static PythonRunResult Fail(string error) => new(false, error);
}

/// <summary>
/// Executes Python using an interpreter embedded in the process.
/// </summary>
/// <remarks>
/// IronPython is a Python implementation written for .NET, so scripts run with no CPython install
/// and no native binary — the same on Windows, macOS and Linux. That is why it is here rather than
/// Python.NET: a hook that only works where an operator happened to install CPython is a feature
/// that mostly does not work.
///
/// The trade is real and worth stating: IronPython is Python 3.4-level and cannot load C-extension
/// packages, so <c>numpy</c> and friends are unavailable. Filter hooks — arithmetic, string and
/// dictionary work over a packet — are squarely within what it does support.
/// </remarks>
public sealed class EmbeddedPythonRuntime
{
    /// <summary>
    /// Creates an interpreter with tracing enabled.
    /// </summary>
    /// <remarks>
    /// <c>Tracing</c> and <c>Frames</c> are not defaults — IronPython compiles without the
    /// per-line callbacks unless asked, and <see cref="ScriptEngine"/>'s trace hook then never
    /// fires. Without them a cancellation token is inert inside a running script, so a timeout can
    /// only abandon a runaway loop while reporting that it stopped it. That costs some speed, and
    /// a plugin sandbox that cannot stop a plugin is not a sandbox.
    /// </remarks>
    public static ScriptEngine CreateEngine() =>
        Python.CreateEngine(new Dictionary<string, object>
        {
            ["Tracing"] = true,
            ["Frames"] = true
        });

    /// <summary>Runs a snippet to completion, capturing syntax and runtime errors.</summary>
    public PythonRunResult Run(string script, ScriptScope? scope = null)
    {
        ScriptEngine engine = CreateEngine();
        return Run(engine, scope ?? engine.CreateScope(), script, CancellationToken.None);
    }

    /// <summary>
    /// Runs a snippet, interrupting it when <paramref name="cancellationToken"/> fires.
    /// </summary>
    /// <remarks>
    /// Cancellation works through IronPython's tracing hook, which the interpreter calls as it
    /// walks the program. Throwing from that callback unwinds the script for real, so
    /// <c>while True: pass</c> actually stops. .NET has no way to abort a thread, so without this
    /// a timeout could only abandon the runaway loop to spin for the life of the process while
    /// reporting that it had been stopped.
    /// </remarks>
    public PythonRunResult Run(
        ScriptEngine engine, ScriptScope scope, string script, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(scope);

        if (cancellationToken.CanBeCanceled)
        {
            // The hook must return itself. In Python's tracing protocol a trace function returning
            // None means "stop tracing this frame", so returning null fired the callback exactly
            // once and then went silent — and a module-level `while True: pass` never leaves its
            // frame, so it never called back again and cancellation was never observed. The timeout
            // then reported that the script had not responded to interruption, which was true, and
            // the reason was that nothing was asking it to.
            IronPython.Runtime.Exceptions.TracebackDelegate? hook = null;
            hook = (frame, kind, payload) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return hook!;
            };

            engine.SetTrace(hook);
        }

        try
        {
            ScriptSource source = engine.CreateScriptSourceFromString(
                script ?? string.Empty, SourceCodeKind.AutoDetect);

            source.Execute(scope);
            return PythonRunResult.Ok();
        }
        catch (OperationCanceledException)
        {
            return PythonRunResult.Fail("Execution was cancelled before the script completed.");
        }
        catch (SyntaxErrorException ex)
        {
            return PythonRunResult.Fail($"Python syntax error (line {ex.Line}): {ex.Message}");
        }
        catch (Exception ex)
        {
            // A raising script, a missing name, an unsupported import. Reported, never rethrown:
            // one bad plugin must not take the host down.
            return PythonRunResult.Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the interpreter, interrupting it if it outlasts its budget.
    /// </summary>
    /// <remarks>
    /// The same trace hook <see cref="Run(ScriptEngine, ScriptScope, string, CancellationToken)"/>
    /// installs, applied to arbitrary interpreter work — loading a plugin file, or calling one of
    /// its functions. Both of those had no budget at all: a <c>.py</c> plugin whose body or whose
    /// hook is <c>while True: pass</c> hung the host, at start-up in the first case and on the
    /// ingest path in the second, and the only cancellation machinery in the codebase sat in a
    /// class nothing constructed.
    /// <para>
    /// Bounded on both sides. The wait for the script is the budget; the wait for it to unwind
    /// afterwards is separately bounded, because a script that ignores the interruption must not be
    /// able to hang the caller by refusing to stop. Which of the two happened is reported, since
    /// "it was stopped" and "it would not stop" call for different actions.
    /// </para>
    /// </remarks>
    public static PythonRunResult RunWithBudget(ScriptEngine engine, TimeSpan budget, Action work)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(work);

        using var cancellation = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;

        Exception? fault = null;
        bool unwound = false;
        Task worker = Task.Run(() =>
        {
            // Installed here, on the thread that is about to run the script. Tracing in Python is
            // per-thread -- it is sys.settrace -- so a hook set on the calling thread is not
            // installed on this one, and the interruption then does nothing at all. Written that
            // way first, and the runaway loop reported "did not respond to interruption" while
            // still burning a core: the caller returned on time and the fiction was that the
            // script had been stopped.
            IronPython.Runtime.Exceptions.TracebackDelegate? hook = null;
            hook = (frame, kind, payload) =>
            {
                token.ThrowIfCancellationRequested();
                return hook!;
            };
            engine.SetTrace(hook);

            // A cancelled script unwound as asked, which is a normal outcome here rather than a
            // fault. Rethrowing it faulted the task, so the wait below threw an AggregateException
            // out of a method whose whole job is to report calmly what happened.
            try { work(); }
            catch (OperationCanceledException) { unwound = true; }
            catch (Exception ex) { fault = ex; }
            finally
            {
                // Best effort: an interrupted thread is unwinding and may not reach this.
                try { engine.SetTrace(null); } catch { /* the thread is going away regardless */ }
            }
        }, CancellationToken.None);

        if (worker.Wait(budget))
        {
            return fault is null
                ? PythonRunResult.Ok()
                : PythonRunResult.Fail($"{fault.GetType().Name}: {fault.Message}");
        }

        cancellation.Cancel();

        // Two conditions, and they are not the same: the task finishing means it stopped, and the
        // flag means it stopped *because it was asked to* rather than by coincidence.
        bool stopped = worker.Wait(UnwindGrace) && unwound;

        return PythonRunResult.Fail(stopped
            ? $"exceeded its {budget.TotalMilliseconds:0} ms budget and was interrupted"
            : $"exceeded its {budget.TotalMilliseconds:0} ms budget and did not respond to interruption");
    }

    /// <summary>How long a cancelled script is given to unwind before the outcome is reported.</summary>
    private static readonly TimeSpan UnwindGrace = TimeSpan.FromSeconds(2);

    /// <summary>Compiles without running, so a malformed hook is rejected before it has effects.</summary>
    public PythonRunResult Validate(string script)
    {
        ScriptEngine engine = CreateEngine();
        try
        {
            engine.CreateScriptSourceFromString(script ?? string.Empty, SourceCodeKind.AutoDetect)
                  .Compile();
            return PythonRunResult.Ok();
        }
        catch (SyntaxErrorException ex)
        {
            return PythonRunResult.Fail($"Python syntax error (line {ex.Line}): {ex.Message}");
        }
        catch (Exception ex)
        {
            return PythonRunResult.Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
