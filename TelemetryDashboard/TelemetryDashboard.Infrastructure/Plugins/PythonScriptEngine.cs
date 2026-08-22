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
/// Exposes a file's functions to the sandbox so a Python hook participates in filtering like any
/// other module. Registering it is what makes a <c>.py</c> file in <c>plugins/</c> actually run
/// rather than being listed as an unsupported extension.
/// </remarks>
public sealed class PythonScriptEngine : IScriptEngine
{
    public string Name => "python";

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".py" };

    /// <summary>Last load failure. Empty when the last load succeeded.</summary>
    public string LastError { get; private set; } = string.Empty;

    /// <summary>How long a plugin file gets to finish importing.</summary>
    /// <remarks>
    /// A budget at all, which is what this did not have. Loading ran the file with no token and no
    /// deadline, so a plugin whose body is <c>while True: pass</c> hung the host at start-up, before
    /// a single packet had been read. The JavaScript engine beside it has carried a budget since it
    /// was written; Python did not, and the only cancellation machinery in the codebase sat in a
    /// class nothing constructed.
    /// <para>
    /// Generous on purpose. Importing is allowed to be slow — a plugin may pull in a library — and
    /// this is a backstop against a script that never finishes, not a performance limit.
    /// </para>
    /// </remarks>
    public TimeSpan LoadTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Budget handed to each module this engine loads.</summary>
    public TimeSpan InvocationTimeout { get; init; } = TimeSpan.FromSeconds(2);

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

        // Through the budgeted path rather than straight at the interpreter: a file that never
        // returns must not take the host with it.
        PythonRunResult loaded = EmbeddedPythonRuntime.RunWithBudget(
            engine, LoadTimeout, () => engine.CreateScriptSourceFromFile(filePath).Execute(scope));

        if (!loaded.Succeeded)
        {
            // Syntax error, raise at import time, an unsupported import, or a body that never
            // finished. The sandbox moves on to the next file rather than holding a
            // half-initialised scope.
            LastError = $"{Path.GetFileName(filePath)}: {loaded.Error}";
            return null;
        }

        List<string> functions = scope.GetVariableNames()
            .Where(name => !baseline.Contains(name))
            .Where(name => scope.TryGetVariable(name, out object? value) && engine.Operations.IsCallable(value))
            .ToList();

        return new PythonScriptModule(filePath, Name, engine, scope, functions)
        {
            InvocationTimeout = InvocationTimeout
        };
    }
}
