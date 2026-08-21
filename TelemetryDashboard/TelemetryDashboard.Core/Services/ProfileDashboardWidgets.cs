using System;
using System.Collections.Generic;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Simulator;

namespace TelemetryDashboard.Core.Services;

/// <summary>
/// Turns a <see cref="MonitoringProfile"/> into the widgets an exported dashboard shows.
/// </summary>
/// <remarks>
/// The exporter's built-in default was one rig written out as JSON — "Edge Temp Sensor (CH-1)",
/// a field called <c>vin</c>, a bus voltage gauge fixed at 0-500 V — so every dashboard anyone
/// exported described that rig whatever they were actually monitoring. The profile already carries
/// each channel's name, unit and range, which is everything a card needs.
/// <para>
/// Two cards per channel and no arbitrary selection: a reading, and its recent shape. Picking "the
/// first channel" for the chart would attach prominence to a quantity for no reason other than its
/// position in a list, which is the same arbitrary attribution the simulator avoids when it refuses
/// to spread channels across nodes by index.
/// </para>
/// </remarks>
public static class ProfileDashboardWidgets
{
    /// <summary>Colours cycled across channels so adjacent cards stay distinguishable.</summary>
    /// <remarks>
    /// Deliberately not the status colours. Green, amber and red mean healthy, warning and alarm
    /// everywhere else in this application, and spending them on channel identity would leave a
    /// card that is red because it is the third channel sitting beside one that is red because
    /// something is wrong.
    /// </remarks>
    private static readonly string[] Palette =
    [
        "#3D8BFF", "#8B7CFF", "#00B8A9", "#C77DFF", "#4DA3FF", "#7DD3C0"
    ];

    /// <summary>Builds the card set for a profile, in the order the profile declares its channels.</summary>
    public static IReadOnlyList<WidgetConfig> For(MonitoringProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var widgets = new List<WidgetConfig>(profile.Channels.Count * 2);

        for (int i = 0; i < profile.Channels.Count; i++)
        {
            ProfileChannel channel = profile.Channels[i];
            string colour = Palette[i % Palette.Length];

            // A gauge needs a range to fill. A channel that declares none gets a plain readout
            // rather than a bar drawn against invented bounds.
            bool bounded = channel.Maximum > channel.Minimum;

            widgets.Add(new WidgetConfig
            {
                Id = $"w-{Slug(channel.Id)}",
                WidgetType = bounded ? "gauge_meter" : "digital_card",
                Title = string.IsNullOrWhiteSpace(channel.Label) ? channel.Id : channel.Label,
                Field = channel.Id,
                Unit = channel.Unit,
                MinLimit = channel.Minimum,
                MaxLimit = channel.Maximum,
                ColorTheme = colour
            });

            widgets.Add(new WidgetConfig
            {
                Id = $"w-{Slug(channel.Id)}-trend",
                WidgetType = "line_chart",
                Title = $"{(string.IsNullOrWhiteSpace(channel.Label) ? channel.Id : channel.Label)} · 추이",
                Field = channel.Id,
                Unit = channel.Unit,
                MinLimit = channel.Minimum,
                MaxLimit = channel.Maximum,
                ColorTheme = colour
            });
        }

        return widgets;
    }

    /// <summary>Reduces a channel id to something safe inside an HTML id attribute.</summary>
    private static string Slug(string channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return "unnamed";

        Span<char> buffer = stackalloc char[channelId.Length];
        for (int i = 0; i < channelId.Length; i++)
        {
            char c = channelId[i];
            buffer[i] = char.IsLetterOrDigit(c) ? c : '-';
        }

        return new string(buffer);
    }
}
