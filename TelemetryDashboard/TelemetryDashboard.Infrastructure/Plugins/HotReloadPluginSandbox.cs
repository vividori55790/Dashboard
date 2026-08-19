using System;
using System.Collections.Generic;
using System.IO;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Plugins;
using TelemetryDashboard.Core.Services;

namespace TelemetryDashboard.Infrastructure.Plugins;

/// <summary>
/// Watches the <c>plugins/</c> folder and hot-reloads filter scripts without restarting the app.
/// </summary>
/// <remarks>
/// Execution is delegated to <see cref="ScriptPluginSandbox"/>; this type owns only the file
/// watching. Previously both types carried their own copy of a no-op <c>ExecuteFilter</c>, so
/// fixing one left the other silently inert.
/// </remarks>
public class HotReloadPluginSandbox : IPluginSandbox, IDisposable
{
    private readonly ScriptPluginSandbox _sandbox;
    private readonly object _debounceLock = new();
    private readonly Dictionary<string, DateTime> _lastSeen = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;

    /// <summary>Editors emit several change notifications per save; collapse them.</summary>
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Creates a sandbox with every language back end this assembly provides.
    /// </summary>
    /// <remarks>
    /// <see cref="ScriptPluginSandbox"/>'s own default registers only the formula and managed
    /// engines, because Core cannot reference the third-party interpreters without taking on their
    /// dependencies. Wiring them here is what makes a <c>.js</c> or <c>.py</c> file in
    /// <c>plugins/</c> actually execute — until this, both engines existed and neither was ever
    /// consulted, so such a file was quietly listed as an unsupported extension.
    /// </remarks>
    public HotReloadPluginSandbox()
        : this(new ScriptPluginSandbox(
            new FormulaScriptEngine(),
            new ManagedAssemblyScriptEngine(),
            new JavaScriptEngine(),
            new PythonScriptEngine()))
    {
    }

    public HotReloadPluginSandbox(ScriptPluginSandbox sandbox)
    {
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
    }

    public string PluginsDirectoryPath => _sandbox.PluginsDirectoryPath;

    public IReadOnlyCollection<string> LoadedPlugins => _sandbox.LoadedPlugins;

    public IReadOnlyDictionary<string, string> UnsupportedPlugins => _sandbox.UnsupportedPlugins;

    public IReadOnlyCollection<string> AvailableFunctions => _sandbox.AvailableFunctions;

    public event EventHandler<string>? PluginReloaded;

    /// <summary>Registers an additional language back end before monitoring begins.</summary>
    public void RegisterEngine(IScriptEngine engine) => _sandbox.RegisterEngine(engine);

    public void StartMonitoring(string pluginsFolderPath)
    {
        if (string.IsNullOrWhiteSpace(pluginsFolderPath)) return;

        Directory.CreateDirectory(pluginsFolderPath);
        _sandbox.SetPluginsDirectory(pluginsFolderPath);
        _sandbox.ReloadAllPlugins();

        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(pluginsFolderPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnPluginFileChanged;
        _watcher.Created += OnPluginFileChanged;
        _watcher.Renamed += OnPluginFileChanged;
    }

    public void StopMonitoring()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    public void LoadPlugin(string scriptFilePath) => _sandbox.LoadPlugin(scriptFilePath);

    public object ExecuteFilter(string functionName, object telemetryPacket) =>
        _sandbox.ExecuteFilter(functionName, telemetryPacket);

    public void ReloadAllPlugins() => _sandbox.ReloadAllPlugins();

    private void OnPluginFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!ShouldHandle(e.FullPath)) return;

        try
        {
            if (!File.Exists(e.FullPath)) return;

            _sandbox.LoadPlugin(e.FullPath);
            PluginReloaded?.Invoke(this, Path.GetFileName(e.FullPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The editor may still hold the file; the next notification will pick it up.
        }
    }

    private bool ShouldHandle(string path)
    {
        lock (_debounceLock)
        {
            DateTime now = DateTime.UtcNow;
            if (_lastSeen.TryGetValue(path, out DateTime previous) && now - previous < DebounceInterval)
            {
                return false;
            }
            _lastSeen[path] = now;
            return true;
        }
    }

    public void Dispose()
    {
        StopMonitoring();
        _sandbox.Dispose();
        GC.SuppressFinalize(this);
    }
}
