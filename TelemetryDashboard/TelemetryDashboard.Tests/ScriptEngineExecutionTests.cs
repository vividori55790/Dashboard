using System.Diagnostics;
using TelemetryDashboard.Core.Plugins;
using TelemetryDashboard.Infrastructure.Plugins;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Proves the JavaScript and Python back ends actually execute, rather than validating a script
/// and reporting that no runtime was available.
/// </summary>
/// <remarks>
/// The specification claimed the sandbox supported "C#, Python, JS". No JavaScript engine existed
/// at all, and the Python path validated syntax and then refused to run. Both are embedded managed
/// interpreters now, so these assertions are about real output, not about a stub's error message.
/// </remarks>
[Collection(HeavyTestCollection.Name)]
public class ScriptEngineExecutionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tdscripts_" + Guid.NewGuid().ToString("N")[..8]);

    public ScriptEngineExecutionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string WriteScript(string name, string body)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, body);
        return path;
    }

    private static ScriptInvocationContext Context(params (string Key, double Value)[] variables) =>
        new(payload: null, variables: variables.ToDictionary(v => v.Key, v => v.Value), nodeId: "NODE-1");

    // ---------------- JavaScript ----------------

    [Fact]
    public void JavaScript_FilterFunction_ReturnsAComputedValue()
    {
        string path = WriteScript("filter.js", "function scaleTemp(ctx) { return ctx.variables.temp * 2 + 1; }");

        using IScriptModule? module = new JavaScriptEngine().Load(path);

        module.Should().NotBeNull();
        module!.TryInvoke("scaleTemp", Context(("temp", 20.5)), out object? result).Should().BeTrue();
        Convert.ToDouble(result).Should().Be(42.0, "20.5 * 2 + 1 — computed by the engine, not by the test");
    }

    [Fact]
    public void JavaScript_ExposesOnlyTheFunctionsTheScriptDefined()
    {
        string path = WriteScript("two.js", "function alpha(c) { return 1; }\nfunction beta(c) { return 2; }");

        using IScriptModule? module = new JavaScriptEngine().Load(path);

        // Built-in globals are subtracted, so Object and Array are not advertised as plugin hooks.
        module!.FunctionNames.Should().BeEquivalentTo(new[] { "alpha", "beta" });
    }

    [Fact]
    public void JavaScript_SyntaxError_IsReportedAndNoModuleIsReturned()
    {
        string path = WriteScript("broken.js", "function oops( { return");
        var engine = new JavaScriptEngine();

        engine.Load(path).Should().BeNull();
        engine.LastError.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void JavaScript_RunawayLoop_IsStoppedByTheStatementLimit()
    {
        string path = WriteScript("spin.js", "function spin(ctx) { while (true) { } }");
        using IScriptModule? module = new JavaScriptEngine { ExecutionTimeout = TimeSpan.FromMilliseconds(300) }.Load(path);

        var clock = Stopwatch.StartNew();
        bool invoked = module!.TryInvoke("spin", Context(), out _);
        clock.Stop();

        // A hostile filter must not hold the ingest path. This is a real interruption: the call
        // returns, and it returns quickly.
        invoked.Should().BeFalse();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void JavaScript_RunawayLoop_IsStoppedWithoutHelpFromTheClock()
    {
        // The test above is named for the statement limit but hands the engine a 300 ms deadline as
        // well, so either guard could have been the one that fired. This one removes the clock from
        // the argument: an hour is longer than any test would wait, so if the call returns at all,
        // the statement ceiling is what returned it.
        //
        // Worth pinning, because the wall-clock timeout was raised from 2 s to 10 s after a valid
        // one-line filter failed to load on a busy machine. That change is only defensible if the
        // load-independent guard genuinely holds on its own.
        string path = WriteScript("spin2.js", "function spin(ctx) { while (true) { } }");
        using IScriptModule? module = new JavaScriptEngine
        {
            ExecutionTimeout = TimeSpan.FromHours(1)
        }.Load(path);

        module.Should().NotBeNull();

        var clock = Stopwatch.StartNew();
        bool invoked = module!.TryInvoke("spin", Context(), out _);
        clock.Stop();

        invoked.Should().BeFalse("a runaway filter has to be refused, not awaited");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30),
            "the statement ceiling is deterministic and does not depend on how busy the machine is");
    }

    [Fact]
    public void JavaScript_AValidScriptThatLoadsSlowly_IsNotReportedAsBroken()
    {
        // The failure this guards against: a plugin that is fine, on a machine that is busy. Jint
        // raised a timeout, Load returned null, and LastError read like a syntax error — so the
        // operator was pointed at a file with nothing wrong in it.
        string path = WriteScript("slow.js", "function f(ctx) { return 1; }");

        // One tick forces the timeout deterministically, without needing a busy machine to borrow.
        var engine = new JavaScriptEngine { ExecutionTimeout = TimeSpan.FromTicks(1) };

        engine.Load(path).Should().BeNull();
        engine.LastError.Should().Contain("limit on time",
            "a timeout is not a fault in the script, and saying so is the difference between "
            + "retrying and hunting for a bug that does not exist");
    }

    // ---------------- Python ----------------

    [Fact]
    public void Python_ActuallyExecutes_AndTheEmbeddedRuntimeIsAlwaysPresent()
    {
        // Moved off PythonNetAdapter, which is gone. The adapter was a timeout wrapper around this
        // runtime, and the timeout it owned is now on the path that actually loads plugins -- so
        // the wrapper was a second entry point to one interpreter, which is the thing to remove
        // rather than to wire.
        var runtime = new EmbeddedPythonRuntime();

        PythonRunResult ran = runtime.Run("x = sum(range(10))\nassert x == 45");

        ran.Succeeded.Should().BeTrue(ran.Error);
        ran.Error.Should().BeEmpty();
    }

    [Fact]
    public void Python_RuntimeError_IsReportedNotThrown()
    {
        var runtime = new EmbeddedPythonRuntime();

        PythonRunResult ran = runtime.Run("raise ValueError('boom')");

        ran.Succeeded.Should().BeFalse();
        ran.Error.Should().Contain("boom");
    }

    // The infinite-loop interruption moved to PythonExecutionBudgetTests, where it exercises the
    // path a plugin actually takes rather than a wrapper nothing constructed.

    [Fact]
    public void SandboxLoadsAndRunsBothJavaScriptAndPythonPluginsFromTheFolder()
    {
        // A .js and a .py file dropped into plugins/ used to be listed as unsupported extensions:
        // the sandbox's default engine set was formula + managed assemblies only, so neither
        // interpreter was ever consulted. This is the wiring, exercised end to end.
        WriteScript("boost.js", "function jsBoost(ctx) { return ctx.variables.temp + 100; }");
        WriteScript("boost.py", """
            def py_boost(ctx):
                return ctx['variables']['temp'] + 200
            """);

        using var sandbox = new HotReloadPluginSandbox();
        sandbox.StartMonitoring(_dir);

        sandbox.UnsupportedPlugins.Should().BeEmpty("both extensions now have an engine");
        sandbox.AvailableFunctions.Should().Contain("jsBoost").And.Contain("py_boost");

        var packet = new Dictionary<string, double> { ["temp"] = 1.5 };

        Convert.ToDouble(sandbox.ExecuteFilter("jsBoost", packet)).Should().Be(101.5);
        Convert.ToDouble(sandbox.ExecuteFilter("py_boost", packet)).Should().Be(201.5);
    }
}
