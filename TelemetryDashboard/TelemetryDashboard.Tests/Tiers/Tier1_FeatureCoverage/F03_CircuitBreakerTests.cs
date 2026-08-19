using System;
using FluentAssertions;
using TelemetryDashboard.Core.Services;
using Xunit;
using TelemetryDashboard.Core.Resilience;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F03_CircuitBreakerTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void CircuitBreaker_DefaultMaxAllowedRate_Is50000()
    {
        var breaker = new TelemetryCircuitBreaker();
        breaker.MaxAllowedRatePerSec.Should().Be(50000);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CircuitBreaker_PacketFlood_IsolatesChannelAndFiresEvent()
    {
        var breaker = new TelemetryCircuitBreaker();
        breaker.MaxAllowedRatePerSec = 100;

        string isolatedChannel = string.Empty;
        breaker.ChannelIsolated += (s, ch) => isolatedChannel = ch;

        for (int i = 0; i <= 101; i++)
        {
            breaker.RecordPacket("CAN_0");
        }

        bool allowed = breaker.AllowPacketProcessing("CAN_0");
        allowed.Should().BeFalse();
        breaker.IsChannelIsolated("CAN_0").Should().BeTrue();
        isolatedChannel.Should().Be("CAN_0");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CircuitBreaker_ReportPacketRate_ExceedingThreshold_TriggersIsolation()
    {
        var breaker = new TelemetryCircuitBreaker();
        breaker.MaxAllowedRatePerSec = 50000;

        breaker.ReportPacketRate("SERIAL_1", 60000);
        breaker.IsChannelIsolated("SERIAL_1").Should().BeTrue();
        breaker.AllowPacketProcessing("SERIAL_1").Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void CircuitBreaker_HighPacketRate_ActivatesUiResourceClamping()
    {
        var breaker = new TelemetryCircuitBreaker();
        breaker.MaxAllowedRatePerSec = 50000;

        for (int i = 0; i < 15000; i++)
        {
            breaker.RecordPacket("STREAM_MAIN");
        }

        breaker.IsUiResourceClamped.Should().BeTrue();
        breaker.SubsampleRatio.Should().BeGreaterThanOrEqualTo(1);
    }
}
