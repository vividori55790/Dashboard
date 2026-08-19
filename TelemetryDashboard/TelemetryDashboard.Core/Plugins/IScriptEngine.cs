using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Plugins;

/// <summary>
/// A loaded script file exposing one or more callable filter functions.
/// </summary>
public interface IScriptModule : IDisposable
{
    /// <summary>Source file this module was loaded from.</summary>
    string SourcePath { get; }

    /// <summary>Engine that produced this module.</summary>
    string EngineName { get; }

    /// <summary>Functions this module exposes.</summary>
    IReadOnlyCollection<string> FunctionNames { get; }

    /// <summary>
    /// Invokes a filter function. Returns false when the function is absent, so the caller can
    /// try the next module rather than treating a miss as a transformation.
    /// </summary>
    bool TryInvoke(string functionName, ScriptInvocationContext context, out object? result);
}

/// <summary>
/// Language back end for the plugin sandbox.
/// </summary>
/// <remarks>
/// This is the extension point that keeps the sandbox current as tooling changes: supporting a new
/// scripting language means registering another engine, with no edit to the sandbox itself. The
/// engines built in are the formula DSL and managed .NET assemblies; a host that wants Python or
/// JavaScript registers an interpreter-backed engine of its own.
/// </remarks>
public interface IScriptEngine
{
    string Name { get; }

    /// <summary>File extensions this engine claims, lower-case and dot-prefixed (e.g. ".dll").</summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <summary>Loads a module, or returns null when the file cannot be compiled or read.</summary>
    IScriptModule? Load(string filePath);
}

/// <summary>
/// Values a filter function may read, plus the packet it is transforming.
/// </summary>
public sealed class ScriptInvocationContext
{
    public ScriptInvocationContext(object? payload, IReadOnlyDictionary<string, double> variables, string nodeId)
    {
        Payload = payload;
        Variables = variables;
        NodeId = nodeId;
    }

    /// <summary>Original object handed to the sandbox.</summary>
    public object? Payload { get; }

    /// <summary>Numeric fields extracted from the payload, addressable by name.</summary>
    public IReadOnlyDictionary<string, double> Variables { get; }

    /// <summary>Node the payload originated from, used to resolve unqualified variables.</summary>
    public string NodeId { get; }

    /// <summary>Resolver in the shape the formula evaluator expects.</summary>
    public double Resolve(string nodeId, string variableName)
    {
        if (Variables.TryGetValue(variableName, out double direct)) return direct;

        string qualified = $"{nodeId}.{variableName}";
        return Variables.TryGetValue(qualified, out double value) ? value : 0.0;
    }
}
