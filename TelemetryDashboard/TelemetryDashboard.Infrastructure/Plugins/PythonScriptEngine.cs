using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Scripting.Hosting;
using TelemetryDashboard.Core.Plugins;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// Loads <c>.py</c> plugin filters onto the embedded Python interpreter.
/// </summary>
/// <remarks>
/// Companion to <see cref="PythonNetAdapter"/>: that type runs a snippet, this one exposes a file's
/// functions to the sandbox so a Python hook participates in filtering like any other module.
/// Registering it is what makes a <c>.py</c> file in <c>plugins/</c> actually run rather than being
/// listed as an unsupported extension.
/// </remarks>
public sealed class PythonScriptEngine : IScriptEngine
{
    public string Name => "python";

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".py" };

    /// <summary>Last load failure. Empty when the last load succeeded.</summary>
    public string LastError { get; private set; } = string.Empty;

    /// <inheritdoc />
    public IScriptModule? Load(string filePath)
    {
        LastError = string.Empty;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            LastError = $"Script not found: {filePath}";
            return null;
        }

        ScriptEngine engine = EmbeddedPythonRuntime.CreateEngine();
        ScriptScope scope = engine.CreateScope();

        // Names present before the file runs are the interpreter's own; the difference afterwards
        // is exactly what the plugin defined.
        var baseline = new HashSet<string>(scope.GetVariableNames(), StringComparer.Ordinal);

        try
        {
            engine.CreateScriptSourceFromFile(filePath).Execute(scope);
        }
        catch (Exception ex)
        {
            // Syntax error, raise at import time, or an unsupported import. The sandbox moves on to
            // the next file rather than holding a half-initialised scope.
            LastError = $"{Path.GetFileName(filePath)}: {ex.Message}";
            return null;
        }

        List<string> functions = scope.GetVariableNames()
            .Where(name => !baseline.Contains(name))
            .Where(name => scope.TryGetVariable(name, out object? value) && engine.Operations.IsCallable(value))
            .ToList();

        return new PythonScriptModule(filePath, Name, engine, scope, functions);
    }
}
