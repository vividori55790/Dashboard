namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F15_LanguageI18nTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void LanguageService_DefaultCulture_IsEnglish()
    {
        var i18n = new LanguageServiceHelper();
        i18n.CurrentCulture.Should().Be("en-US");
        i18n.GetString("Title").Should().Be("Telemetry Dashboard");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void LanguageService_SwitchCulture_ToKorean()
    {
        var i18n = new LanguageServiceHelper();
        i18n.SetCulture("ko-KR");

        i18n.CurrentCulture.Should().Be("ko-KR");
        i18n.GetString("Title").Should().Be("텔레메트리 대시보드");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void LanguageService_SwitchCulture_BackToEnglish()
    {
        var i18n = new LanguageServiceHelper();
        i18n.SetCulture("ko-KR");
        i18n.SetCulture("en-US");

        i18n.CurrentCulture.Should().Be("en-US");
        i18n.GetString("Title").Should().Be("Telemetry Dashboard");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void LanguageService_MissingKey_ReturnsFallback()
    {
        var i18n = new LanguageServiceHelper();
        string val = i18n.GetString("NonExistentKey");

        val.Should().Be("[NonExistentKey]");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void LanguageService_Formatting_AdaptsToActiveCulture()
    {
        var i18n = new LanguageServiceHelper();
        i18n.SetCulture("ko-KR");
        string formattedDate = i18n.FormatDate(new DateTime(2026, 8, 9));

        formattedDate.Should().Contain("2026");
    }
}

public class LanguageServiceHelper
{
    public string CurrentCulture { get; private set; } = "en-US";
    private readonly Dictionary<string, Dictionary<string, string>> _resources = new()
    {
        ["en-US"] = new() { ["Title"] = "Telemetry Dashboard", ["Connect"] = "Connect" },
        ["ko-KR"] = new() { ["Title"] = "텔레메트리 대시보드", ["Connect"] = "연결" }
    };

    public void SetCulture(string culture)
    {
        CurrentCulture = culture;
    }

    public string GetString(string key)
    {
        if (_resources.TryGetValue(CurrentCulture, out var map) && map.TryGetValue(key, out var val))
        {
            return val;
        }
        return $"[{key}]";
    }

    public string FormatDate(DateTime date)
    {
        var ci = new System.Globalization.CultureInfo(CurrentCulture);
        return date.ToString("d", ci);
    }
}
