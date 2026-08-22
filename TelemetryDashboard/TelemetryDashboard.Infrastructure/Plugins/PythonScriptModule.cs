using System;
using System.Collections.Generic;
using Microsoft.Scripting.Hosting;
using TelemetryDashboard.Core.Plugins;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// One loaded Python file and the scope holding the names it defined.
/// </summary>
/// <remarks>
/// Each module gets its own scope. Sharing one would let two plugins from different authors
/// overwrite each other's helpers, and a fault in either would surface as a fault in the wrong one.
/// </remarks>
public sealed class PythonScriptModule : IScriptModule
{
    private readonly ScriptEngine _engine;
    private readonly ScriptScope _scope;
    private readonly List<string> _functions;
    private bool _disposed;

    internal PythonScriptModule(
        string sourcePath, string engineName, ScriptEngine engine, ScriptScope scope, IEnumerable<string> functions)
    {
        SourcePath = sourcePath;
        EngineName = engineName;
        _engine = engine;
        _scope = scope;
        _functions = new List<string>(functions);
    }

    public string SourcePath { get; }

    public string EngineName { get; }

    public IReadOnlyCollection<string> FunctionNames => _functions;

    /// <summary>Message from the last failed invocation. Empty after a successful one.</summary>
    public string LastError { get; private set; } = string.Empty;

    /// <summary>How long one call into this module gets.</summary>
    /// <remarks>
    /// Tighter than the load budget and for a different reason. A filter runs on the ingest path,
    /// once per packet, so a hook that spins does not merely delay a plugin — it stops the console,
    /// the recording and every other channel's scoring behind it. There was no budget here at all.
    /// </remarks>
    public TimeSpan InvocationTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public bool TryInvoke(string functionName, ScriptInvocationContext context, out object? result)
    {
        result = null;
        LastError = string.Empty;

        if (_disposed || string.IsNullOrWhiteSpace(functionName)) return false;
        if (!_scope.TryGetVariable(functionName, out object? target)) return false;
        if (!_engine.Operations.IsCallable(target)) return false;

        object? invoked = null;
        PythonRunResult outcome = EmbeddedPythonRuntime.RunWithBudget(
            _engine, InvocationTimeout,
            () => invoked = _engine.Operations.Invoke(target, BuildArgument(context)));

        if (!outcome.Succeeded)
        {
            // A raising filter, or one that ran past its budget. Either is a failed invocation and
            // not a host failure: the packet must still reach every other subscriber.
            LastError = $"{functionName}: {outcome.Error}";
            return false;
        }

        result = invoked;
        return true;
    }

    /// <summary>
    /// Hands the hook a dict: <c>{'nodeId': ..., 'payload': ..., 'variables': {...}}</c>, so a
    /// filter reads <c>ctx['variables']['temp']</c>.
    /// </summary>
    private static Dictionary<string, object?> BuildArgument(ScriptInvocationContext context)
    {
        var variables = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, double> entry in context.Variables)
        {
            variables[entry.Key] = entry.Value;
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["nodeId"] = context.NodeId,
            ["payload"] = context.Payload,
            ["variables"] = variables
        };
    }

    public void Dispose() => _disposed = true;
}
