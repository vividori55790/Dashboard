using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace TelemetryDashboard.UI.Services;

public class LanguageService : INotifyPropertyChanged
{
    private static readonly HashSet<string> SupportedCultures = new(StringComparer.OrdinalIgnoreCase) { "en-US", "ko-KR" };
    /// <summary>
    /// The language the application starts in.
    /// </summary>
    /// <remarks>
    /// Korean, because that is the dictionary App.xaml merges and therefore what is on screen. It
    /// used to say en-US while every caption was Korean, so the first toggle asked to switch to the
    /// language it believed it was already in.
    /// </remarks>
    public const string DefaultCulture = "ko-KR";

    private CultureInfo _currentCulture = new CultureInfo(DefaultCulture);

    public CultureInfo CurrentCulture => _currentCulture;
    public string CurrentCultureName => _currentCulture.Name;

    public event EventHandler? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetLanguage(string cultureCode)
    {
        if (string.IsNullOrWhiteSpace(cultureCode) || !SupportedCultures.Contains(cultureCode))
        {
            cultureCode = "en-US";
        }

        _currentCulture = new CultureInfo(cultureCode);

        // Before the event, so a handler that re-reads a caption sees the new one.
        UiStrings.Apply(cultureCode);

        LanguageChanged?.Invoke(this, EventArgs.Empty);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public void SetCulture(string cultureCode) => SetLanguage(cultureCode);

    /// <summary>The caption <paramref name="key"/> names in the active language.</summary>
    /// <remarks>
    /// Read from the same dictionary the markup reads, rather than from a second table in C#. There
    /// used to be one of those, with six keys no screen ever asked for -- so it could not disagree
    /// with the interface, because it was never part of it. One table is the only arrangement where
    /// a translation fixed in one place is fixed everywhere.
    /// <para>
    /// An unknown key comes back as itself. On screen that reads as an untranslated caption, which
    /// is findable; returning empty would render as a blank button.
    /// </para>
    /// </remarks>
    public string GetString(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        return System.Windows.Application.Current?.TryFindResource(key) as string ?? key;
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
