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

    // ---------------- Python ----------------

    [Fact]
    public void Python_ActuallyExecutes_AndTheEmbeddedRuntimeIsAlwaysPresent()
    {
        var adapter = new PythonNetAdapter();

        adapter.IsInterpreterAvailable.Should().BeTrue();
        adapter.UsesHostInterpreter.Should().BeFalse("no host override is attached in this test");

        adapter.ExecuteScript("x = sum(range(10))\nassert x == 45", out string error).Should().BeTrue(error);
        error.Should().BeEmpty();
    }

    [Fact]
    public void Python_RuntimeError_IsReportedNotThrown()
    {
        var adapter = new PythonNetAdapter();

        adapter.ExecuteScript("raise ValueError('boom')", out string error).Should().BeFalse();
        error.Should().Contain("boom");
    }

    [Fact]
    public void Python_InfiniteLoop_IsGenuinelyInterrupted_NotAbandoned()
    {
        var adapter = new PythonNetAdapter();
        var clock = Stopwatch.StartNew();

        bool ran = adapter.ExecuteWithTimeout("while True:\n    pass", timeoutMs: 200);
        clock.Stop();

        ran.Should().BeFalse();

        // "interrupted" rather than "did not respond": the tracing hook unwound the loop. The
        // distinction is the whole point — abandoning the thread would leave it spinning for the
        // life of the process while this method reported the script stopped.
        adapter.LastError.Should().Contain("interrupted");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    // ---------------- End to end through the real sandbox ----------------

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
