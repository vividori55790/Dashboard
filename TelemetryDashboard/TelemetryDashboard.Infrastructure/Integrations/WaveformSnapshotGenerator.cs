using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Infrastructure.Integrations;

/// <summary>
/// High-density ASCII sparkline and SVG vector waveform generator for telemetry alerts.
/// </summary>
public static class WaveformSnapshotGenerator
{
    private static readonly char[] SparklineChars = new[] { ' ', '▂', '▃', '▄', '▅', '▆', '▇', '█' };

    /// <summary>
    /// Generates an 8-level Unicode block sparkline string representing time-series data.
    /// </summary>
    public static string GenerateAsciiSparkline(IEnumerable<double>? samples)
    {
        if (samples == null) return string.Empty;
        var arr = samples.ToArray();
        if (arr.Length == 0) return string.Empty;

        double min = arr.Min();
        double max = arr.Max();
        double range = max - min;

        var sb = new StringBuilder(arr.Length);
        if (range < 1e-9)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                sb.Append(SparklineChars[3]);
            }
            return sb.ToString();
        }

        foreach (double val in arr)
        {
            int idx = (int)Math.Clamp(Math.Floor((val - min) / range * 7.0), 0, 7);
            sb.Append(SparklineChars[idx]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates standalone SVG polyline markup for the waveform.
    /// </summary>
    public static string GenerateSvgWaveform(IEnumerable<double>? samples, int width = 300, int height = 60, string strokeColor = "#FF2E63")
    {
        if (samples == null)
        {
            return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\"></svg>";
        }

        var arr = samples.ToArray();
        if (arr.Length == 0)
        {
            return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\"></svg>";
        }

        double min = arr.Min();
        double max = arr.Max();
        double range = Math.Max(1e-9, max - min);
        double padY = 4.0;
        double usableH = Math.Max(1.0, height - (padY * 2));

        var points = new StringBuilder();
        for (int i = 0; i < arr.Length; i++)
        {
            double x = arr.Length == 1 ? 0 : (double)i / (arr.Length - 1) * width;
            double normY = (arr[i] - min) / range;
            double y = height - padY - (normY * usableH);
            points.Append(CultureInfo.InvariantCulture, $"{x:F1},{y:F1} ");
        }

        string pointsStr = points.ToString().TrimEnd();
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\"><polyline fill=\"none\" stroke=\"{strokeColor}\" stroke-width=\"2\" points=\"{pointsStr}\" /></svg>";
    }

    /// <summary>
    /// Computes summary metrics (Min, Max, Mean, Peak-to-Peak, StdDev, Count) across sample points.
    /// </summary>
    public static WaveformStats ComputeStats(IEnumerable<double>? samples)
    {
        if (samples == null) return new WaveformStats();
        var arr = samples.ToArray();
        if (arr.Length == 0) return new WaveformStats();

        double min = arr.Min();
        double max = arr.Max();
        double mean = arr.Average();
        double sumSqDiff = arr.Sum(v => Math.Pow(v - mean, 2));
        double stdDev = Math.Sqrt(sumSqDiff / arr.Length);

        return new WaveformStats
        {
            Min = min,
            Max = max,
            Mean = mean,
            PeakToPeak = max - min,
            StdDev = stdDev,
            Count = arr.Length
        };
    }
}
