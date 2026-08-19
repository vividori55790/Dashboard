using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Plugins;

namespace TelemetryDashboard.Core.Plugins;

/// <summary>
/// Hot-reloading plugin sandbox: loads filter scripts from <c>plugins/</c> and executes them
/// against live telemetry.
/// </summary>
/// <remarks>
/// Languages are supplied by <see cref="IScriptEngine"/> implementations, so the sandbox itself
/// never changes as tooling evolves. The formula DSL and managed .NET assemblies ship in the box;
/// a host may register an interpreter-backed engine for other languages.
/// <para>
/// A file whose extension no registered engine claims is reported through
/// <see cref="UnsupportedPlugins"/> rather than being cached and silently ignored, so an operator
/// can see that a plugin is not running instead of assuming it is.
/// </para>
/// </remarks>
public class ScriptPluginSandbox : IPluginSandbox, IDisposable
{
    private readonly List<IScriptEngine> _engines = new();
    private readonly ConcurrentDictionary<string, IScriptModule> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _unsupported = new(StringComparer.OrdinalIgnoreCase);

    public ScriptPluginSandbox() : this(new FormulaScriptEngine(), new ManagedAssemblyScriptEngine())
    {
    }

    public ScriptPluginSandbox(params IScriptEngine[] engines)
    {
        _engines.AddRange(engines ?? Array.Empty<IScriptEngine>());
    }

    /// <summary>Directory watched for plugin files.</summary>
    public string PluginsDirectoryPath { get; private set; } = string.Empty;

    /// <summary>Successfully loaded module file names.</summary>
    public IReadOnlyCollection<string> LoadedPlugins => _modules.Keys.ToList();

    /// <summary>Files present in the plugin folder that no registered engine can execute.</summary>
    public IReadOnlyDictionary<string, string> UnsupportedPlugins => _unsupported;

    /// <summary>Every function callable across all loaded modules.</summary>
    public IReadOnlyCollection<string> AvailableFunctions =>
        _modules.Values.SelectMany(m => m.FunctionNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public event EventHandler<string>? PluginLoaded;

    /// <summary>Registers an additional language back end.</summary>
    public void RegisterEngine(IScriptEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engines.Add(engine);
    }

    public void SetPluginsDirectory(string directory) => PluginsDirectoryPath = directory ?? string.Empty;

    public void LoadPlugin(string scriptFilePath)
    {
        if (string.IsNullOrWhiteSpace(scriptFilePath) || !File.Exists(scriptFilePath)) return;

        string key = Path.GetFileName(scriptFilePath);
        string extension = Path.GetExtension(scriptFilePath);

        IScriptEngine? engine = _engines.FirstOrDefault(
            e => e.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));

        if (engine is null)
        {
            _unsupported[key] = $"No engine registered for '{extension}'.";
            return;
        }

        IScriptModule? module = engine.Load(scriptFilePath);
        if (module is null)
        {
            _unsupported[key] = $"Engine '{engine.Name}' could not load the file.";
            return;
        }

        // Replace atomically and unload whatever was there before.
        if (_modules.TryRemove(key, out IScriptModule? previous)) previous.Dispose();

        _modules[key] = module;
        _unsupported.TryRemove(key, out _);
        PluginLoaded?.Invoke(this, key);
    }

    /// <summary>
    /// Runs a named filter across loaded modules and returns the transformed payload.
    /// When no module exposes the function the payload is returned unchanged.
    /// </summary>
    public object ExecuteFilter(string functionName, object telemetryPacket)
    {
        if (string.IsNullOrWhiteSpace(functionName)) return telemetryPacket;

        ScriptInvocationContext context = BuildContext(telemetryPacket);

        foreach (IScriptModule module in _modules.Values)
        {
            if (module.TryInvoke(functionName, context, out object? result) && result is not null)
            {
                return Project(telemetryPacket, result);
            }
        }

        return telemetryPacket;
    }

    public void ReloadAllPlugins()
    {
        foreach (IScriptModule module in _modules.Values) module.Dispose();
        _modules.Clear();
        _unsupported.Clear();

        if (string.IsNullOrWhiteSpace(PluginsDirectoryPath) || !Directory.Exists(PluginsDirectoryPath)) return;

        foreach (string file in Directory.GetFiles(PluginsDirectoryPath))
        {
            LoadPlugin(file);
        }
    }

    public void Dispose()
    {
        foreach (IScriptModule module in _modules.Values) module.Dispose();
        _modules.Clear();
        GC.SuppressFinalize(this);
    }

    /// <summary>Extracts the numeric surface of a payload so scripts can address it by name.</summary>
    private static ScriptInvocationContext BuildContext(object? payload)
    {
        var variables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        string nodeId = string.Empty;

        switch (payload)
        {
            case TelemetryPacket packet:
                nodeId = packet.NodeId;
                variables["value"] = packet.Value;
                if (!string.IsNullOrWhiteSpace(packet.Variable)) variables[packet.Variable] = packet.Value;
                break;

            case IReadOnlyDictionary<string, double> numeric:
                foreach (KeyValuePair<string, double> entry in numeric) variables[entry.Key] = entry.Value;
                break;

            case string:
                break; // a bare string carries no addressable numeric fields

            case not null:
                foreach (PropertyInfo property in payload.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    // Indexers need arguments and cannot be read as plain values.
                    if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;

                    object? raw = property.GetValue(payload);
                    if (raw is IConvertible && raw is not string && raw is not bool)
                    {
                        try { variables[property.Name] = Convert.ToDouble(raw); }
                        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { }
                    }
                    else if (raw is string text && property.Name.Equals("NodeId", StringComparison.OrdinalIgnoreCase))
                    {
                        nodeId = text;
                    }
                }
                break;
        }

        return new ScriptInvocationContext(payload, variables, nodeId);
    }

    /// <summary>Writes a scalar result back into the shape the caller supplied.</summary>
    private static object Project(object original, object result)
    {
        if (original is TelemetryPacket packet && result is double value)
        {
            return new TelemetryPacket(packet.NodeId, packet.Variable, value, packet.Unit, packet.Timestamp,
                packet.Flags | PacketFlags.IsDerived);
        }

        return result;
    }
}
