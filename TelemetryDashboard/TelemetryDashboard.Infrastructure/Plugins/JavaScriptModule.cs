using System;
using System.Collections.Generic;
using System.Linq;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using TelemetryDashboard.Core.Plugins;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// One loaded JavaScript file and the engine instance that holds its globals.
/// </summary>
/// <remarks>
/// The engine is per-module rather than shared. Two plugins from different authors would otherwise
/// see each other's globals — one could redefine the other's helper, and a bug in either would
/// look like a bug in the wrong plugin.
/// </remarks>
public sealed class JavaScriptModule : IScriptModule
{
    private readonly Engine _engine;
    private readonly List<string> _functions;
    private bool _disposed;

    internal JavaScriptModule(string sourcePath, string engineName, Engine engine, IEnumerable<string> functionNames)
    {
        SourcePath = sourcePath;
        EngineName = engineName;
        _engine = engine;
        _functions = functionNames.ToList();
    }

    public string SourcePath { get; }

    public string EngineName { get; }

    public IReadOnlyCollection<string> FunctionNames => _functions;

    /// <summary>Message from the last failed invocation. Empty after a successful one.</summary>
    public string LastError { get; private set; } = string.Empty;

    /// <inheritdoc />
    public bool TryInvoke(string functionName, ScriptInvocationContext context, out object? result)
    {
        result = null;
        LastError = string.Empty;

        if (_disposed || string.IsNullOrWhiteSpace(functionName)) return false;

        JsValue target = _engine.GetValue(functionName);
        if (target is not Function) return false;

        try
        {
            JsValue argument = BuildArgument(context);
            result = _engine.Invoke(functionName, argument).ToObject();
            return true;
        }
        catch (Exception ex)
        {
            // A throwing filter, or one that hit the time or statement limit. Reported as a failed
            // invocation rather than rethrown: one bad plugin must not stop the packet reaching
            // every other subscriber.
            LastError = $"{functionName}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Shapes the invocation context as a plain JS object: <c>{ nodeId, payload, variables }</c>.
    /// </summary>
    private JsValue BuildArgument(ScriptInvocationContext context)
    {
        var variables = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, double> entry in context.Variables)
        {
            variables[entry.Key] = entry.Value;
        }

        return JsValue.FromObject(_engine, new
        {
            nodeId = context.NodeId,
            payload = context.Payload,
            variables
        });
    }

    /// <summary>
    /// Callable globals, enumerated by asking the script engine rather than reading Jint's
    /// internals — <c>Engine.Realm</c> is not public API and would tie this to one Jint version.
    /// </summary>
    internal static HashSet<string> GlobalFunctionNames(Engine engine)
    {
        object? raw = engine
            .Evaluate("Object.getOwnPropertyNames(globalThis).filter(function (k) { return typeof globalThis[k] === 'function'; })")
            .ToObject();

        var names = new HashSet<string>(StringComparer.Ordinal);
        if (raw is object[] entries)
        {
            foreach (object? entry in entries)
            {
                if (entry is string name) names.Add(name);
            }
        }

        return names;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.Dispose();
    }
}
