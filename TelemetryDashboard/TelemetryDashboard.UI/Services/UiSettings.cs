using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TelemetryDashboard.UI.Services;

/// <summary>
/// The handful of preferences that have to survive a restart.
/// </summary>
/// <remarks>
/// There was nowhere to put these. No Properties.Settings, no app.config, no registry use, no
/// settings file — the only persisted UI state in the whole application was the dock layout, saved
/// explicitly by the operator. So the theme button toggled an enum in memory and the language
/// button set a CultureInfo field, and both were gone at the next launch even in the version where
/// they did something. A preference that does not persist is not a preference.
/// <para>
/// JSON in the per-user local data folder, written whole. There are three values; a schema, a
/// migration path and a change-notification layer would all be larger than the thing they manage.
/// </para>
/// </remarks>
public sealed class UiSettings
{
    private static readonly JsonSerializerOptions Format = new() { WriteIndented = true };

    /// <summary>"Dark" or "Light". Anything else reads as Dark.</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>UI culture, e.g. <c>ko-KR</c>.</summary>
    public string Language { get; set; } = "ko-KR";

    /// <summary>
    /// Rules file describing the device on this bench, or empty for the built-in framing.
    /// </summary>
    /// <remarks>
    /// Remembered for the same reason the theme is. An operator whose MCU calls its rail Vout picks
    /// that file once; asking again at every launch would make the mapping feel like a workaround
    /// rather than the installation's configuration, which is what it is.
    /// </remarks>
    public string WireRulesPath { get; set; } = string.Empty;

    /// <summary>Where these came from, and where <see cref="Save"/> writes them back to.</summary>
    /// <remarks>
    /// Carried on the object rather than assumed, because the alternative is a service that cannot
    /// be exercised without writing over the settings of whoever is running the tests. Not
    /// serialised: a file that records its own location is a file that is wrong the moment it is
    /// copied.
    /// </remarks>
    [JsonIgnore]
    public string Origin { get; set; } = DefaultPath;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TelemetryDashboard", "settings.json");

    /// <summary>Reads the settings, falling back to the defaults for anything unreadable.</summary>
    /// <remarks>
    /// A damaged settings file gives a default-looking application rather than one that will not
    /// start. Preferences are not worth a crash, and a first launch is indistinguishable from a
    /// corrupt file here on purpose — both mean "nothing has been chosen yet".
    /// </remarks>
    public static UiSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            UiSettings settings = File.Exists(path)
                ? JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(path)) ?? new UiSettings()
                : new UiSettings();
            settings.Origin = path;
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UiSettings { Origin = path };
        }
    }

    /// <summary>Writes the settings. Returns why it could not be written, or null.</summary>
    public string? Save(string? path = null)
    {
        path ??= Origin;
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Format));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }
}
