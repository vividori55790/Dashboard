using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using TelemetryDashboard.Core.Plugins;
using TelemetryDashboard.Infrastructure.Plugins;

namespace TelemetryDashboard.UI.Dialogs;

/// <summary>One file in the plugins folder, as the loader found it.</summary>
public class PluginItemModel
{
    /// <summary>File name on disk.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Engine that claims this extension, or a statement that none does.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Whether the loader holds a module for this file.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Last write time of the file itself.</summary>
    public string LastModified { get; set; } = string.Empty;

    /// <summary>The loader's reason when a file did not load; otherwise its size.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Lists what the plugin loader found in <c>plugins/</c> and what it could do with each file.
/// </summary>
/// <remarks>
/// The grid was previously a literal list of three plugins — <c>MovingAverageFilter.cs</c>,
/// <c>PowerEfficiencyCalc.py</c>, <c>ThermalDeratingAlert.js</c> — all reported ACTIVE, on a folder
/// that was usually empty. Two of them named behaviour the application does not have, and the first
/// could not have run at all: no registered engine claims <c>.cs</c>. Reload then showed
/// "successfully recompiled and hot-reloaded" without consulting the loader.
/// <para>
/// Every row here is a real directory entry, and its state comes from
/// <see cref="ScriptPluginSandbox.LoadedPlugins"/> or <see cref="ScriptPluginSandbox.UnsupportedPlugins"/>,
/// so a file that is present but not running is visible as exactly that.
/// </para>
/// </remarks>
public partial class PluginSandboxDialog : Window
{
    /// <summary>Extensions the registered engines claim, for naming the engine per file.</summary>
    /// <remarks>
    /// Mirrors the engines <see cref="HotReloadPluginSandbox"/> constructs. It is presentation only:
    /// the loader, not this table, decides what runs, and a file this table cannot name still shows
    /// whatever verdict the loader reached.
    /// </remarks>
    private static readonly Dictionary<string, string> EngineByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".formula"] = "수식 (formula)",
            [".rule"] = "수식 (formula)",
            [".calc"] = "수식 (formula)",
            [".dll"] = ".NET 어셈블리",
            [".js"] = "JavaScript",
            [".mjs"] = "JavaScript",
            [".py"] = "Python"
        };

    private readonly HotReloadPluginSandbox _sandbox = new();
    private readonly string _pluginsDir;

    public PluginSandboxDialog()
    {
        InitializeComponent();

        _pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
        Directory.CreateDirectory(_pluginsDir);

        // The watcher reloads on save; the grid follows it, so the "no restart needed" line in the
        // header describes something the operator can watch happen rather than a promise.
        _sandbox.PluginReloaded += OnPluginReloaded;

        _sandbox.StartMonitoring(_pluginsDir);
        LoadPlugins();
    }

    /// <summary>Refreshes the grid when the file watcher reloads a module off the UI thread.</summary>
    private void OnPluginReloaded(object? sender, string fileName) =>
        Dispatcher.InvokeAsync(LoadPlugins);

    /// <summary>
    /// Rebuilds the grid from the directory and the loader's two result sets.
    /// </summary>
    /// <remarks>
    /// The directory is the source of rows rather than <see cref="ScriptPluginSandbox.LoadedPlugins"/>,
    /// so a file that failed to load is still listed. Showing only what loaded would hide the case
    /// the operator most needs to see: a plugin they installed that is not running.
    /// </remarks>
    private void LoadPlugins()
    {
        TxtPluginsPath.Text = _pluginsDir;

        var loaded = new HashSet<string>(_sandbox.LoadedPlugins, StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, string> unsupported = _sandbox.UnsupportedPlugins;

        string[] files;
        try
        {
            files = Directory.GetFiles(_pluginsDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DgPlugins.ItemsSource = Array.Empty<PluginItemModel>();
            TxtPluginCount.Text = $"플러그인 폴더를 읽을 수 없습니다: {ex.Message}";
            return;
        }

        List<PluginItemModel> items = files
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(path => Describe(path, loaded, unsupported))
            .ToList();

        DgPlugins.ItemsSource = items;

        int running = items.Count(i => loaded.Contains(i.Name));
        IReadOnlyCollection<string> functions = _sandbox.AvailableFunctions;

        if (items.Count == 0)
        {
            TxtPluginCount.Text = "폴더가 비어 있습니다. 지원 확장자: .formula, .rule, .calc, .dll, .js, .py";
            return;
        }

        string functionSummary = functions.Count == 0
            ? "호출 가능한 함수 없음"
            : $"호출 가능한 함수 {functions.Count}개: {string.Join(", ", functions.Take(8))}" +
              (functions.Count > 8 ? " …" : string.Empty);

        TxtPluginCount.Text = $"파일 {items.Count}개 중 {running}개 적재됨 · {functionSummary}";
    }

    /// <summary>Builds one row, taking the verdict from the loader rather than the extension.</summary>
    private static PluginItemModel Describe(
        string path,
        HashSet<string> loaded,
        IReadOnlyDictionary<string, string> unsupported)
    {
        string name = Path.GetFileName(path);
        string extension = Path.GetExtension(path);

        var item = new PluginItemModel
        {
            Name = name,
            Language = EngineByExtension.TryGetValue(extension, out string? engine)
                ? engine
                : $"'{extension}' 담당 엔진 없음"
        };

        try
        {
            var info = new FileInfo(path);
            item.LastModified = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
            item.Description = $"{info.Length:N0} bytes";
        }
        catch (IOException)
        {
            item.LastModified = "—";
            item.Description = "—";
        }

        if (loaded.Contains(name))
        {
            item.Status = "적재됨";
        }
        else if (unsupported.TryGetValue(name, out string? reason))
        {
            item.Status = "실행 안 됨";
            item.Description = reason;
        }
        else
        {
            // Present on disk but in neither result set: the loader has not attempted it since the
            // last reload. Saying so is more use than guessing which way it would go.
            item.Status = "미확인";
            item.Description = "마지막 재적재 이후 처리되지 않았습니다.";
        }

        return item;
    }

    /// <summary>
    /// Reloads every file and reports the loader's actual result.
    /// </summary>
    /// <remarks>
    /// The old handler showed "All plugins successfully recompiled and hot-reloaded into memory"
    /// regardless of outcome — including on an empty folder, and including for files that had just
    /// been rejected. The counts below come from the loader after it has run.
    /// </remarks>
    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        _sandbox.ReloadAllPlugins();
        LoadPlugins();

        int loadedCount = _sandbox.LoadedPlugins.Count;
        int rejected = _sandbox.UnsupportedPlugins.Count;

        TxtPluginCount.Text = rejected == 0
            ? $"{DateTime.Now:HH:mm:ss} 재적재 완료 — {loadedCount}개 적재됨."
            : $"{DateTime.Now:HH:mm:ss} 재적재 완료 — {loadedCount}개 적재, {rejected}개 실행 불가 (사유는 비고 열 참조).";
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _pluginsDir,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"폴더를 열 수 없습니다: {ex.Message}",
                "폴더 열기 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        // The watcher and every loaded module belong to this dialog's loader instance.
        _sandbox.PluginReloaded -= OnPluginReloaded;
        _sandbox.Dispose();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
