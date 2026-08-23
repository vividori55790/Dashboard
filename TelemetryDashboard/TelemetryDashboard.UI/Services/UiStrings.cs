using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.Windows;

namespace TelemetryDashboard.UI.Services;

/// <summary>
/// Swapping the dictionary the ribbon's captions are read from.
/// </summary>
/// <remarks>
/// The captions were literals in MainWindow.xaml, so switching language changed the culture, raised
/// an event nobody handled, wrote "Language switched to en-US" into the log, and left every word on
/// screen exactly as it was. A button that reports success and does nothing is worse than one that
/// is not there.
/// <para>
/// <c>DynamicResource</c> rather than a binding, so the swap reaches the markup with no code behind
/// it and the existing architecture rule — every resource key the markup asks for must be defined
/// somewhere — covers the keys for free.
/// </para>
/// </remarks>
public static class UiStrings
{
    /// <summary>Folder the per-culture dictionaries live in, relative to the assembly.</summary>
    public const string Folder = "Resources";

    /// <summary>Cultures a dictionary ships for.</summary>
    public static IReadOnlyList<string> Supported { get; } = ["en-US", "ko-KR"];

    /// <summary>
    /// Points the application's captions at <paramref name="culture"/>.
    /// </summary>
    /// <remarks>
    /// Does nothing without an <see cref="Application"/>, which is the case in a test and in the
    /// headless host. Returns false there rather than throwing: a language that cannot be applied
    /// is not a reason to fail, and the caller has nothing useful to do about it.
    /// <para>
    /// The old dictionary is removed before the new one is merged, and both are matched by source
    /// path rather than by position. Merged dictionaries resolve last-wins, so leaving the previous
    /// one in place would work by accident until something else was merged after it.
    /// </para>
    /// </remarks>
    public static bool Apply(string culture)
    {
        if (Application.Current is not { } app) return false;
        if (!Supported.Contains(culture, StringComparer.OrdinalIgnoreCase)) return false;

        Collection<ResourceDictionary> merged = app.Resources.MergedDictionaries;

        foreach (ResourceDictionary existing in merged.Where(IsStrings).ToList())
        {
            merged.Remove(existing);
        }

        merged.Add(new ResourceDictionary { Source = SourceFor(culture) });
        return true;
    }

    /// <summary>Uri of the dictionary for <paramref name="culture"/>.</summary>
    /// <remarks>
    /// An absolute pack uri. A relative one resolves against the markup that declared it, which is
    /// how App.xaml can name these by folder -- and a ResourceDictionary constructed in code has no
    /// declaring markup, so the same relative string silently resolves to nothing and the swap
    /// appears to succeed while changing not one caption on screen. Measured: the ribbon stayed
    /// Korean through a switch that reported success.
    /// </remarks>
    public static Uri SourceFor(string culture) =>
        new($"pack://application:,,,/{Folder}/Strings.{culture}.xaml", UriKind.Absolute);

    /// <summary>Whether a merged dictionary is one of the caption sets.</summary>
    private static bool IsStrings(ResourceDictionary dictionary) =>
        dictionary.Source is { } source
        && source.OriginalString.Contains($"{Folder}/Strings.", StringComparison.OrdinalIgnoreCase);
}
