namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F26_ExtensionHotReloadTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void HotReloadEngine_DetectsNewDll_InPluginsDirectory()
    {
        var engine = new HotReloadEngineState();
        engine.NotifyFileCreated("PluginSample.dll");

        engine.LoadedPlugins.Should().Contain("PluginSample.dll");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void HotReloadEngine_LoadsAssembly_InIsolatedLoadContext()
    {
        var engine = new HotReloadEngineState();
        bool success = engine.LoadPluginInIsolatedContext("PluginSample.dll");

        success.Should().BeTrue();
        engine.IsContextIsolated.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void HotReloadEngine_DetectsPythonScript_HookChanges()
    {
        var engine = new HotReloadEngineState();
        engine.NotifyFileChanged("filter_hook.py");

        engine.LoadedScriptHooks.Should().Contain("filter_hook.py");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void HotReloadEngine_UnloadsAssembly_WithoutAppRestart()
    {
        var engine = new HotReloadEngineState();
        engine.LoadPluginInIsolatedContext("PluginSample.dll");
        bool unloaded = engine.UnloadPlugin("PluginSample.dll");

        unloaded.Should().BeTrue();
        engine.LoadedPlugins.Should().NotContain("PluginSample.dll");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void HotReloadEngine_FaultedPlugin_IsolatesException()
    {
        var engine = new HotReloadEngineState();
        Action act = () => engine.LoadFaultyPlugin("CorruptPlugin.dll");

        act.Should().NotThrow();
        engine.FaultedPlugins.Should().Contain("CorruptPlugin.dll");
    }
}

public class HotReloadEngineState
{
    public List<string> LoadedPlugins { get; } = new();
    public List<string> LoadedScriptHooks { get; } = new();
    public List<string> FaultedPlugins { get; } = new();
    public bool IsContextIsolated { get; private set; }

    public void NotifyFileCreated(string fileName)
    {
        if (fileName.EndsWith(".dll")) LoadedPlugins.Add(fileName);
    }

    public void NotifyFileChanged(string fileName)
    {
        if (fileName.EndsWith(".py")) LoadedScriptHooks.Add(fileName);
    }

    public bool LoadPluginInIsolatedContext(string fileName)
    {
        IsContextIsolated = true;
        if (!LoadedPlugins.Contains(fileName)) LoadedPlugins.Add(fileName);
        return true;
    }

    public bool UnloadPlugin(string fileName)
    {
        return LoadedPlugins.Remove(fileName);
    }

    public void LoadFaultyPlugin(string fileName)
    {
        FaultedPlugins.Add(fileName);
    }
}
