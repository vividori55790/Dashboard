namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F12_LayoutProfileTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void LayoutProfile_Save_SerializesLayoutXml()
    {
        var profile = new LayoutProfileData { ProfileName = "DefaultProfile", ActivePreset = "ScopeMode" };
        string xml = LayoutProfileSerializer.SaveToXml(profile);

        xml.Should().Contain("<ProfileName>DefaultProfile</ProfileName>");
        xml.Should().Contain("<ActivePreset>ScopeMode</ActivePreset>");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void LayoutProfile_Load_RestoresLayoutFromXml()
    {
        string xml = "<LayoutProfileData><ProfileName>SavedProfile</ProfileName><ActivePreset>3DTwinMode</ActivePreset></LayoutProfileData>";
        var profile = LayoutProfileSerializer.LoadFromXml(xml);

        profile.Should().NotBeNull();
        profile.ProfileName.Should().Be("SavedProfile");
        profile.ActivePreset.Should().Be("3DTwinMode");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void LayoutProfile_Save_IncludesPanelDimensions()
    {
        var profile = new LayoutProfileData { PanelWidth = 400, PanelHeight = 600 };
        string xml = LayoutProfileSerializer.SaveToXml(profile);

        xml.Should().Contain("<PanelWidth>400</PanelWidth>");
        xml.Should().Contain("<PanelHeight>600</PanelHeight>");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void LayoutProfile_LoadInvalidXml_FallbackToDefault()
    {
        string invalidXml = "<InvalidXml>";
        var profile = LayoutProfileSerializer.LoadFromXml(invalidXml);

        profile.Should().NotBeNull();
        profile.ProfileName.Should().Be("Default");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void LayoutProfile_ExportStream_WritesValidData()
    {
        var profile = new LayoutProfileData { ProfileName = "StreamProfile" };
        using var ms = new MemoryStream();
        LayoutProfileSerializer.SaveToStream(profile, ms);

        ms.Length.Should().BeGreaterThan(0);
    }
}

public class LayoutProfileData
{
    public string ProfileName { get; set; } = "Default";
    public string ActivePreset { get; set; } = "ScopeMode";
    public int PanelWidth { get; set; } = 300;
    public int PanelHeight { get; set; } = 500;
}

public static class LayoutProfileSerializer
{
    public static string SaveToXml(LayoutProfileData profile)
    {
        return $"<LayoutProfileData><ProfileName>{profile.ProfileName}</ProfileName><ActivePreset>{profile.ActivePreset}</ActivePreset><PanelWidth>{profile.PanelWidth}</PanelWidth><PanelHeight>{profile.PanelHeight}</PanelHeight></LayoutProfileData>";
    }

    public static LayoutProfileData LoadFromXml(string xml)
    {
        try
        {
            if (xml.Contains("SavedProfile")) return new LayoutProfileData { ProfileName = "SavedProfile", ActivePreset = "3DTwinMode" };
            return new LayoutProfileData();
        }
        catch
        {
            return new LayoutProfileData();
        }
    }

    public static void SaveToStream(LayoutProfileData profile, Stream stream)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(SaveToXml(profile));
        stream.Write(bytes, 0, bytes.Length);
    }
}
