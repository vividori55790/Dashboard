using System;
using System.Threading;
using System.Threading.Tasks;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// Runs Python filter hooks on an interpreter embedded in this process.
/// </summary>
/// <remarks>
/// This type used to validate a script's syntax and then report that no runtime was available,
/// because the dashboard shipped without one — honest, but it meant Python hooks were a documented
/// feature that never executed anything.
/// <para>
/// It now executes for real through IronPython, a Python implementation written for .NET. Nothing
/// has to be installed on the machine and nothing is platform-specific, so a hook behaves the same
/// on Windows, macOS and Linux. The limit worth knowing: IronPython is Python 3.4-level and cannot
/// load C-extension packages, so <c>numpy</c> and similar are unavailable — arithmetic, string and
/// dictionary work over a packet, which is what a filter hook does, is fully supported.
/// </para>
/// <para>
/// <see cref="Interpreter"/> remains as an override for a host that would rather drive CPython
/// through Python.NET and needs the C extensions.
/// </para>
/// </remarks>
public sealed class PythonNetAdapter
{
    private readonly EmbeddedPythonRuntime _runtime = new();

    /// <summary>
    /// Optional host-supplied execution hook, taking precedence over the embedded interpreter.
    /// Returns <c>null</c> on success, or a diagnostic describing the failure.
    /// </summary>
    /// <remarks>
    /// A host wiring CPython here owns cancellation: .NET cannot abort a thread spinning inside
    /// native code, so a runaway script stops only if the hook itself polls the token.
    /// </remarks>
    public Func<string, CancellationToken, string?>? Interpreter { get; set; }

    /// <summary>Always true: an interpreter is embedded, whether or not a host overrides it.</summary>
    public bool IsInterpreterAvailable => true;

    /// <summary>True when a host override is in force rather than the embedded interpreter.</summary>
    public bool UsesHostInterpreter => Interpreter is not null;

    /// <summary>Diagnostic from the most recent call, or empty after a success.</summary>
    public string LastError { get; private set; } = string.Empty;

    /// <summary>Compiles and runs <paramref name="script"/>.</summary>
    /// <param name="errorMessage">Reason for the failure; empty on success.</param>
    public bool ExecuteScript(string script, out string errorMessage) =>
        Execute(script, CancellationToken.None, out errorMessage);

    /// <summary>
    /// Runs <paramref name="script"/>, stopping it if it outlasts <paramref name="timeoutMs"/>.
    /// </summary>
    /// <remarks>
    /// On the embedded interpreter this is a real interruption, not an abandonment: cancellation
    /// reaches IronPython's tracing hook, which unwinds the script, so <c>while True: pass</c>
    /// genuinely stops instead of spinning for the life of the process while being reported as
    /// stopped. A host override can only be interrupted if it cooperates with the token.
    /// </remarks>
    public bool ExecuteWithTimeout(string script, int timeoutMs)
    {
        using var cancellation = new CancellationTokenSource();
        Task<bool> worker = Task.Run(() => Execute(script, cancellation.Token, out _), CancellationToken.None);

        bool finished;
        try
        {
            finished = worker.Wait(Math.Max(0, timeoutMs));
        }
        catch (AggregateException ex)
        {
            LastError = $"Python execution faulted: {ex.GetBaseException().Message}";
            return false;
        }

        if (finished) return worker.Result;

        cancellation.Cancel();

        // Give the trace hook a moment to unwind before reporting. Waiting forever would hand a
        // hostile script the ability to hang the caller, so the wait is bounded and the outcome is
        // stated either way.
        bool unwound = worker.Wait(TimeSpan.FromSeconds(2));
        LastError = unwound
            ? $"Python script exceeded its {timeoutMs} ms budget and was interrupted."
            : $"Python script exceeded its {timeoutMs} ms budget and did not respond to interruption.";
        return false;
    }

    private bool Execute(string script, CancellationToken cancellationToken, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            errorMessage = LastError = "The script is empty.";
            return false;
        }

        Func<string, CancellationToken, string?>? host = Interpreter;
        if (host is not null)
        {
            try
            {
                string? failure = host(script, cancellationToken);
                errorMessage = LastError = failure ?? string.Empty;
                return failure is null;
            }
            catch (Exception ex)
            {
                errorMessage = LastError = $"Python execution failed: {ex.Message}";
                return false;
            }
        }

        ScriptEngine engine = EmbeddedPythonRuntime.CreateEngine();
        PythonRunResult result = _runtime.Run(engine, engine.CreateScope(), script, cancellationToken);

        errorMessage = LastError = result.Error;
        return result.Succeeded;
    }
}
