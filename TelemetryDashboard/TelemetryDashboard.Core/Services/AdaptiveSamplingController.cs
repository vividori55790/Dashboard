using System;
using System.Collections.Concurrent;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Models;

namespace TelemetryDashboard.Core.Services;

/// <summary>
/// Dynamic Adaptive Sampling Controller.
/// Adjusts telemetry sampling frequency automatically from Nominal rate (default 1 Hz) to Burst rate (default 1000 Hz)
/// upon detecting Z-Score anomaly spikes (>= 2.5 sigma), with hysteresis cooldown timing and sample decimation.
/// </summary>
public class AdaptiveSamplingController : IAdaptiveSamplingController
{
    private class ChannelState
    {
        public string ChannelId { get; set; } = string.Empty;
        public SamplingMode Mode { get; set; } = SamplingMode.Nominal;
        public int RateHz { get; set; } = 1;
        public DateTime? BurstStartTime { get; set; }
        public DateTime? LastAnomalyTime { get; set; }
        public DateTime? LastSampleTime { get; set; }
        public int ConsecutiveNormalSamples { get; set; }
    }

    private readonly ConcurrentDictionary<string, ChannelState> _channelStates = new(StringComparer.OrdinalIgnoreCase);

    public int BaseRateHz { get; set; } = 1;
    public int BurstRateHz { get; set; } = 1000;
    public double AnomalyThresholdSigma { get; set; } = 2.5;
    public double CooldownDurationSec { get; set; } = 5.0;
    public double MinBurstDurationSec { get; set; } = 2.0;

    public event EventHandler<SamplingRateChangedEventArgs>? SamplingRateChanged;

    public int GetSamplingRate(string channelId)
    {
        if (_channelStates.TryGetValue(channelId, out var state))
        {
            return state.RateHz;
        }
        return BaseRateHz;
    }

    public SamplingMode GetSamplingMode(string channelId)
    {
        if (_channelStates.TryGetValue(channelId, out var state))
        {
            return state.Mode;
        }
        return SamplingMode.Nominal;
    }

    public int EvaluateSamplingRate(string channelId, double zScore)
    {
        return EvaluateSamplingRate(channelId, zScore, DateTime.UtcNow);
    }

    public int EvaluateSamplingRate(string channelId, double zScore, DateTime timestamp)
    {
        var state = _channelStates.GetOrAdd(channelId, id => new ChannelState
        {
            ChannelId = id,
            Mode = SamplingMode.Nominal,
            RateHz = BaseRateHz
        });

        lock (state)
        {
            int oldRate = state.RateHz;
            SamplingMode oldMode = state.Mode;
            bool isAnomaly = Math.Abs(zScore) >= AnomalyThresholdSigma;

            if (isAnomaly)
            {
                if (state.Mode != SamplingMode.Burst)
                {
                    state.BurstStartTime = timestamp;
                }
                state.Mode = SamplingMode.Burst;
                state.RateHz = BurstRateHz;
                state.LastAnomalyTime = timestamp;
                state.ConsecutiveNormalSamples = 0;
            }
            else
            {
                state.ConsecutiveNormalSamples++;

                if (state.Mode == SamplingMode.Burst || state.Mode == SamplingMode.Cooldown)
                {
                    double elapsedSinceBurst = state.BurstStartTime.HasValue
                        ? (timestamp - state.BurstStartTime.Value).TotalSeconds
                        : double.MaxValue;

                    double elapsedSinceLastAnomaly = state.LastAnomalyTime.HasValue
                        ? (timestamp - state.LastAnomalyTime.Value).TotalSeconds
                        : double.MaxValue;

                    if (CooldownDurationSec > 0 && (elapsedSinceBurst < MinBurstDurationSec || elapsedSinceLastAnomaly < CooldownDurationSec))
                    {
                        state.Mode = SamplingMode.Cooldown;
                        state.RateHz = BurstRateHz; // Maintain burst rate during cooldown window
                    }
                    else
                    {
                        // Cooldown expired or CooldownDurationSec == 0: revert to nominal
                        state.Mode = SamplingMode.Nominal;
                        state.RateHz = BaseRateHz;
                        state.BurstStartTime = null;
                        state.LastAnomalyTime = null;
                    }
                }
                else
                {
                    state.Mode = SamplingMode.Nominal;
                    state.RateHz = BaseRateHz;
                }
            }

            if (state.RateHz != oldRate || state.Mode != oldMode)
            {
                SamplingRateChanged?.Invoke(this, new SamplingRateChangedEventArgs
                {
                    ChannelId = channelId,
                    OldRateHz = oldRate,
                    NewRateHz = state.RateHz,
                    Mode = state.Mode,
                    TriggerZScore = zScore,
                    Timestamp = timestamp
                });
            }

            return state.RateHz;
        }
    }

    public bool ShouldSample(string channelId, long sampleCounter)
    {
        int rate = GetSamplingRate(channelId);
        if (rate >= BurstRateHz) return true; // Sample everything at or above burst rate

        int skipFactor = Math.Max(1, BurstRateHz / Math.Max(1, rate));
        return sampleCounter % skipFactor == 0;
    }

    public bool ShouldSample(string channelId, DateTime timestamp)
    {
        int rate = GetSamplingRate(channelId);
        if (rate >= BurstRateHz) return true;

        var state = _channelStates.GetOrAdd(channelId, id => new ChannelState
        {
            ChannelId = id,
            Mode = SamplingMode.Nominal,
            RateHz = BaseRateHz
        });

        lock (state)
        {
            double minIntervalSec = 1.0 / Math.Max(1, rate);
            if (!state.LastSampleTime.HasValue || (timestamp - state.LastSampleTime.Value).TotalSeconds >= minIntervalSec - 1e-4)
            {
                state.LastSampleTime = timestamp;
                return true;
            }
            return false;
        }
    }

    public string FormatRateCommand(string channelOrNodeId, int rateHz)
    {
        if (string.IsNullOrWhiteSpace(channelOrNodeId))
        {
            return $"$CMD,RATE,{rateHz}\n";
        }
        return $"$CMD,RATE,{channelOrNodeId},{rateHz}\n";
    }

    public void ResetChannel(string channelId)
    {
        if (_channelStates.TryGetValue(channelId, out var state))
        {
            lock (state)
            {
                state.Mode = SamplingMode.Nominal;
                state.RateHz = BaseRateHz;
                state.BurstStartTime = null;
                state.LastAnomalyTime = null;
                state.ConsecutiveNormalSamples = 0;
            }
        }
    }

    public void ResetAll()
    {
        _channelStates.Clear();
    }
}
