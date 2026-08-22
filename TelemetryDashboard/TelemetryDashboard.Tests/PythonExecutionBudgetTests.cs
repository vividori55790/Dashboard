using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using TelemetryDashboard.Core.Plugins;
using TelemetryDashboard.Infrastructure.Plugins;
using Xunit;

namespace TelemetryDashboard.Tests;

/// <summary>
/// A plugin script that never finishes must not take the host with it.
/// </summary>
/// <remarks>
/// There was no budget on either Python path. Loading ran the file with no token and no deadline,
/// so a plugin whose body is <c>while True: pass</c> hung the host at start-up before a packet had
/// been read; invoking a hook had the same hole, on the ingest path, once per packet. The
/// JavaScript engine beside it has carried a budget since it was written, and the only cancellation
/// machinery in the codebase sat in <c>PythonNetAdapter</c>, which nothing constructed.
/// <para>
/// The interruption is real rather than an abandonment: it works through IronPython's tracing hook,
/// which unwinds the script. .NET cannot abort a thread, so without that a timeout could only walk
/// away and let the loop spin for the life of the process while reporting it had been stopped —
/// which is why these tests assert on the wording as well as on the clock.
/// </para>
/// </remarks>
[Collection("HeavyTests")]
public class PythonExecutionBudgetTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "tdpy_" + Guid.NewGuid().ToString("N")[..8]);

    public PythonExecutionBudgetTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Plugin(string name, string body)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, body);
        return path;
    }

    /// <summary>Budgets short enough that a hang is a failed test rather than a hung suite.</summary>
    private static PythonScriptEngine Engine() => new()
    {
        LoadTimeout = TimeSpan.FromMilliseconds(600),
        InvocationTimeout = TimeSpan.FromMilliseconds(600)
    };

    [Fact]
    [Trait("Category", "Tier2")]
    public void APluginWhoseBodyNeverFinishesIsRefusedRatherThanHangingTheLoad()
    {
        string path = Plugin("runaway_body.py", "while True:\n    pass\n");
        var engine = Engine();

        var clock = Stopwatch.StartNew();
        IScriptModule? loaded = engine.Load(path);
        clock.Stop();

        loaded.Should().BeNull();
        engine.LastError.Should().Contain("budget");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(6),
            "the load budget plus the unwind grace, not the life of the process");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void TheRefusalSaysWhetherTheScriptActuallyStopped()
    {
        // "It was stopped" and "it would not stop" call for different actions -- the second means
        // a thread is still spinning and the process wants restarting.
        string path = Plugin("runaway_body2.py", "while True:\n    pass\n");
        var engine = Engine();

        engine.Load(path);

        // Recorded in the failure text so a run says which of the two happened rather than only
        // that one of them did.
        engine.LastError.Should().Match<string>(e =>
            e.Contains("was interrupted") || e.Contains("did not respond to interruption"),
            "the outcome was: " + engine.LastError);

        engine.LastError.Should().Contain("was interrupted",
            "IronPython's trace hook is supposed to unwind the script for real; if this reads "
            + "'did not respond', the loop is still spinning and the interruption is a fiction");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AHookThatSpinsIsRefusedRatherThanHangingTheIngestPath()
    {
        // The worse of the two holes: this runs once per packet, so a hook that never returns
        // stops the console, the recording and every other channel's scoring behind it.
        string path = Plugin("runaway_hook.py", "def on_packet(ctx):\n    while True:\n        pass\n");
        var engine = Engine();

        IScriptModule? module = engine.Load(path);
        module.Should().NotBeNull(engine.LastError);

        var clock = Stopwatch.StartNew();
        bool ok = module!.TryInvoke("on_packet", new ScriptInvocationContext("{}", new Dictionary<string, double>(), "N"), out _);
        clock.Stop();

        ok.Should().BeFalse();
        ((PythonScriptModule)module).LastError.Should().Contain("budget");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(6));
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public void AWellBehavedPluginIsUnaffected()
    {
        // The budget must not cost anything a working plugin can notice.
        string path = Plugin("good.py", "def on_packet(ctx):\n    return ctx['nodeId']\n");
        var engine = Engine();

        IScriptModule? module = engine.Load(path);
        module.Should().NotBeNull();

        bool ok = module!.TryInvoke("on_packet", new ScriptInvocationContext("{}", new Dictionary<string, double>(), "MCU_1"), out object? result);

        ok.Should().BeTrue(((PythonScriptModule)module).LastError);
        result.Should().Be("MCU_1");
    }

    [Fact]
    [Trait("Category", "Tier2")]
    public async Task OneRunawayPluginDoesNotStopTheNextOneLoading()
    {
        // The sandbox loads a directory. A file that never finishes must cost its own budget and
        // nothing else, or one bad plugin disables every plugin behind it in the listing.
        string bad = Plugin("aaa_bad.py", "while True:\n    pass\n");
        string good = Plugin("zzz_good.py", "def hook(ctx):\n    return 1\n");
        var engine = Engine();

        IScriptModule? second = await Task.Run(() =>
        {
            engine.Load(bad);
            return engine.Load(good);
        });
        second.Should().NotBeNull("the runaway file is refused, and the next one still loads");
        second!.FunctionNames.Should().Contain("hook");
    }
}
