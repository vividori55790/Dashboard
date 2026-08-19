using System;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Interfaces;

/// <summary>
/// Interface for dynamic adaptive sampling controller.
/// Automatically transitions between Nominal rate (default 1Hz) and Burst rate (default 1000Hz)
/// on anomaly detection with hysteresis cooldown timing and sample decimation.
/// </summary>
public interface IAdaptiveSamplingController
{
    int BaseRateHz { get; set; }
    int BurstRateHz { get; set; }
    double AnomalyThresholdSigma { get; set; }
    double CooldownDurationSec { get; set; }
    double MinBurstDurationSec { get; set; }

    event EventHandler<SamplingRateChangedEventArgs>? SamplingRateChanged;

    int GetSamplingRate(string channelId);
    SamplingMode GetSamplingMode(string channelId);
    int EvaluateSamplingRate(string channelId, double zScore);
    int EvaluateSamplingRate(string channelId, double zScore, DateTime timestamp);
    bool ShouldSample(string channelId, long sampleCounter);
    bool ShouldSample(string channelId, DateTime timestamp);
    string FormatRateCommand(string channelOrNodeId, int rateHz);
    void ResetChannel(string channelId);
    void ResetAll();
}
