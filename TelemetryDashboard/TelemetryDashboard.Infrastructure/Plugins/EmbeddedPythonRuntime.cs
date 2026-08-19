using System;
using System.Collections.Generic;
using System.Threading;
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
            engine.SetTrace((frame, kind, payload) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return null!;
            });
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
