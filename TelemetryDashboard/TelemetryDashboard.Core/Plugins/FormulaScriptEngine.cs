using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TelemetryDashboard.Core.Services;

namespace TelemetryDashboard.Core.Plugins;

/// <summary>
/// Executes declarative filter scripts through the built-in formula evaluator.
/// </summary>
/// <remarks>
/// Script format — one named function per section:
/// <code>
/// # comment
/// [to_fahrenheit]
/// value * 1.8 + 32
///
/// [power_watts]
/// voltage * current
/// </code>
/// Expressions may reference any numeric field of the packet, optionally node-qualified
/// (<c>COM3.temp</c>). This runs real arithmetic against real packet data — the previous sandbox
/// read plugin files into a dictionary and returned the packet untouched, so every plugin
/// appeared to load and none of them did anything.
/// </remarks>
public sealed class FormulaScriptEngine : IScriptEngine
{
    private static readonly string[] Extensions = { ".formula", ".rule", ".calc" };

    public string Name => "formula";

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public IScriptModule? Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

        var functions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string current = Path.GetFileNameWithoutExtension(filePath);
        var expression = new List<string>();

        foreach (string rawLine in File.ReadAllLines(filePath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//")) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Commit(functions, current, expression);
                current = line[1..^1].Trim();
                continue;
            }

            expression.Add(line);
        }

        Commit(functions, current, expression);

        return functions.Count == 0 ? null : new FormulaScriptModule(filePath, Name, functions);
    }

    private static void Commit(IDictionary<string, string> functions, string name, List<string> lines)
    {
        if (lines.Count > 0 && !string.IsNullOrWhiteSpace(name))
        {
            functions[name] = string.Join(' ', lines);
        }
        lines.Clear();
    }

    private sealed class FormulaScriptModule : IScriptModule
    {
        private readonly FormulaEvaluator _evaluator = new();
        private readonly IReadOnlyDictionary<string, string> _functions;

        public FormulaScriptModule(string sourcePath, string engineName, IReadOnlyDictionary<string, string> functions)
        {
            SourcePath = sourcePath;
            EngineName = engineName;
            _functions = functions;
        }

        public string SourcePath { get; }

        public string EngineName { get; }

        public IReadOnlyCollection<string> FunctionNames => _functions.Keys.ToList();

        public bool TryInvoke(string functionName, ScriptInvocationContext context, out object? result)
        {
            result = null;
            if (!_functions.TryGetValue(functionName ?? string.Empty, out string? expression)) return false;

            try
            {
                result = _evaluator.Evaluate(expression, context.NodeId, context.Resolve);
                return true;
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or DivideByZeroException)
            {
                // A malformed rule must not take the ingest pipeline down with it.
                return false;
            }
        }

        public void Dispose() { }
    }
}
