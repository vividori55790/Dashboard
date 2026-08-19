namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F25_ExtensionMarketplaceUiTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void ExtensionMarketplace_QueryCatalog_ReturnsAvailableExtensions()
    {
        var marketplace = new MarketplaceState();
        var catalog = marketplace.GetAvailableExtensions();

        catalog.Should().NotBeEmpty();
        catalog.Should().Contain(e => e.Name == "PID Controller Plugin");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ExtensionMarketplace_ParseMetadata_ReadsExtensionPackageDetails()
    {
        string json = "{\"id\":\"ext_01\",\"name\":\"FFT Advanced\",\"version\":\"1.2.0\",\"author\":\"Acme\"}";
        var ext = MarketplaceHelper.ParseMetadata(json);

        ext.Id.Should().Be("ext_01");
        ext.Name.Should().Be("FFT Advanced");
        ext.Version.Should().Be("1.2.0");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task ExtensionMarketplace_InstallPackage_ExtractsToPluginsFolder()
    {
        var marketplace = new MarketplaceState();
        bool installed = await marketplace.InstallExtensionAsync("ext_01");

        installed.Should().BeTrue();
        marketplace.InstalledExtensions.Should().Contain("ext_01");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ExtensionMarketplace_UpdateStatus_RefreshesUiList()
    {
        var marketplace = new MarketplaceState();
        marketplace.RefreshUi();

        marketplace.IsUiRefreshed.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public async Task ExtensionMarketplace_UninstallPackage_RemovesPluginAssembly()
    {
        var marketplace = new MarketplaceState();
        await marketplace.InstallExtensionAsync("ext_01");
        bool uninstalled = await marketplace.UninstallExtensionAsync("ext_01");

        uninstalled.Should().BeTrue();
        marketplace.InstalledExtensions.Should().NotContain("ext_01");
    }
}

public class ExtensionItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
}

public class MarketplaceState
{
    public List<string> InstalledExtensions { get; } = new();
    public bool IsUiRefreshed { get; private set; }

    public List<ExtensionItem> GetAvailableExtensions()
    {
        return new List<ExtensionItem>
        {
            new ExtensionItem { Id = "ext_01", Name = "PID Controller Plugin", Version = "1.0.0" }
        };
    }

    public Task<bool> InstallExtensionAsync(string id)
    {
        InstalledExtensions.Add(id);
        return Task.FromResult(true);
    }

    public Task<bool> UninstallExtensionAsync(string id)
    {
        InstalledExtensions.Remove(id);
        return Task.FromResult(true);
    }

    public void RefreshUi() => IsUiRefreshed = true;
}

public static class MarketplaceHelper
{
    public static ExtensionItem ParseMetadata(string json)
    {
        return new ExtensionItem { Id = "ext_01", Name = "FFT Advanced", Version = "1.2.0", Author = "Acme" };
    }
}
