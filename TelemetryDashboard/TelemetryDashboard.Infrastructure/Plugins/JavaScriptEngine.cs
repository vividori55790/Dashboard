using System;
using System.Collections.Generic;
using System.IO;
using Jint;
using TelemetryDashboard.Core.Plugins;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// Runs plugin filters written in JavaScript.
/// </summary>
/// <remarks>
/// The specification claimed the sandbox supported "C#, Python, JS" while no JavaScript engine
/// existed anywhere in the codebase, so a <c>.js</c> file dropped into <c>plugins/</c> was ignored
/// in silence — indistinguishable, from the operator's side, from a script that ran and did
/// nothing.
///
/// Jint is an interpreter written in C#, which is what makes this honest across platforms: no Node
/// install, no native binary, identical behaviour on Windows, macOS and Linux. An engine that
/// shelled out to a system interpreter would have turned scripting into a feature that works only
/// where someone remembered to install one.
///
/// Every module gets its own <see cref="Engine"/> with explicit limits, because plugin code is
/// untrusted by definition and a runaway <c>while(true)</c> in a filter must not take the hub down.
/// </remarks>
public sealed class JavaScriptEngine : IScriptEngine
{
    /// <summary>Wall-clock ceiling for one script evaluation or function call.</summary>
    /// <remarks>
    /// A backstop, not the primary guard. <see cref="MaxStatements"/> is what actually bounds a
    /// runaway plugin, and it does so deterministically — the same script hits the same limit on
    /// every machine. Wall time does not: it measures how busy the computer is as much as what the
    /// script does.
    /// <para>
    /// This was two seconds, which was short enough for an ordinary machine to trip it. Loading a
    /// one-line filter took eleven seconds while the test suite was running a storage benchmark
    /// alongside it; Jint raised a timeout, <see cref="Load"/> returned null, and the sandbox moved
    /// on. From the operator's side a perfectly valid plugin was simply absent, for a reason that
    /// had nothing to do with the plugin. Ten seconds is still a ceiling no reasonable filter
    /// approaches, and it is far enough above ordinary scheduling noise that tripping it means
    /// something.
    /// </para>
    /// </remarks>
    public TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Memory ceiling for one engine instance.</summary>
    public long MemoryLimitBytes { get; init; } = 8L * 1024 * 1024;

    /// <summary>Statement ceiling, which catches a tight loop the timeout would only catch later.</summary>
    /// <remarks>
    /// The load-independent half of the sandbox. A <c>while(true)</c> reaches two hundred thousand
    /// statements in well under a second whatever else the machine is doing, so this fires first on
    /// the case that matters and fires identically everywhere.
    /// </remarks>
    public int MaxStatements { get; init; } = 200_000;

    public string Name => "javascript";

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".js", ".mjs" };

    /// <summary>Last load failure, for diagnostics. Empty when the last load succeeded.</summary>
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

        string source;
        try
        {
            source = File.ReadAllText(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"Could not read {filePath}: {ex.Message}";
            return null;
        }

        var engine = new Engine(options => options
            .TimeoutInterval(ExecutionTimeout)
            .LimitMemory(MemoryLimitBytes)
            .MaxStatements(MaxStatements)
            .Strict());

        HashSet<string> builtIns;
        HashSet<string> afterLoad;
        try
        {
            // Snapshot the built-in globals first, so what the script adds is exactly the
            // difference. Listing every callable global instead would advertise Object, Array and
            // the rest of the standard library as plugin entry points.
            builtIns = JavaScriptModule.GlobalFunctionNames(engine);
            engine.Execute(source);
            afterLoad = JavaScriptModule.GlobalFunctionNames(engine);
        }
        catch (Exception ex)
        {
            // A syntax error, a throw at top level, or a limit hit while loading. Report it and
            // return null: the sandbox then moves to the next module rather than holding a
            // half-initialised engine that would fail unpredictably on first call.
            //
            // A timeout says so in as many words, because it is the one failure here that is not
            // about the script. Reporting "filter.js: The operation has timed out" alongside the
            // syntax errors invites the operator to go looking for a fault in a file that does not
            // have one.
            LastError = ex is TimeoutException
                ? $"{Path.GetFileName(filePath)}: took longer than {ExecutionTimeout.TotalSeconds:0.#}s to load. "
                  + "This is a limit on time, not a fault in the script; retrying on a less busy machine may load it."
                : $"{Path.GetFileName(filePath)}: {ex.Message}";

            engine.Dispose();
            return null;
        }

        afterLoad.ExceptWith(builtIns);
        return new JavaScriptModule(filePath, Name, engine, afterLoad);
    }

    /// <summary>Compiles a snippet without a file, for validating an editor buffer.</summary>
    public bool TryValidate(string source, out string error)
    {
        error = string.Empty;
        using var engine = new Engine(options => options
            .TimeoutInterval(ExecutionTimeout)
            .MaxStatements(MaxStatements));

        try
        {
            engine.Execute(source ?? string.Empty);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
