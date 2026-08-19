using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace TelemetryDashboard.UI.Services;

public class LanguageService : INotifyPropertyChanged
{
    private static readonly HashSet<string> SupportedCultures = new(StringComparer.OrdinalIgnoreCase) { "en-US", "ko-KR" };
    private CultureInfo _currentCulture = new CultureInfo("en-US");

    public CultureInfo CurrentCulture => _currentCulture;
    public string CurrentCultureName => _currentCulture.Name;

    public event EventHandler? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly Dictionary<string, Dictionary<string, string>> _resources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en-US"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = "Telemetry Dashboard",
            ["Connect"] = "Connect",
            ["Disconnect"] = "Disconnect",
            ["OperatorMode"] = "Operator View",
            ["EngineerMode"] = "Engineer Mode",
            ["WelcomeUser"] = "Welcome, {0}!"
        },
        ["ko-KR"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = "텔레메트리 대시보드",
            ["Connect"] = "연결",
            ["Disconnect"] = "연결 해제",
            ["OperatorMode"] = "운용자 뷰",
            ["EngineerMode"] = "엔지니어 모드",
            ["WelcomeUser"] = "환영합니다, {0}님!"
        }
    };

    public void SetLanguage(string cultureCode)
    {
        if (string.IsNullOrWhiteSpace(cultureCode) || !SupportedCultures.Contains(cultureCode))
        {
            cultureCode = "en-US";
        }

        _currentCulture = new CultureInfo(cultureCode);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public void SetCulture(string cultureCode) => SetLanguage(cultureCode);

    public string GetString(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        string langKey = SupportedCultures.Contains(_currentCulture.Name) ? _currentCulture.Name : "en-US";
        if (_resources.TryGetValue(langKey, out var map) && map.TryGetValue(key, out var val))
        {
            return val;
        }

        if (langKey != "en-US" && _resources["en-US"].TryGetValue(key, out var defaultVal))
        {
            return defaultVal;
        }

        return key;
    }

    public string GetFormattedString(string key, params object[] args)
    {
        string template = GetString(key);
        if (string.IsNullOrEmpty(template) || args == null || args.Length == 0) return template;
        try
        {
            return string.Format(_currentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public string FormatDate(DateTime date)
    {
        return date.ToString("d", _currentCulture);
    }

    public string this[string key] => GetString(key);
}
