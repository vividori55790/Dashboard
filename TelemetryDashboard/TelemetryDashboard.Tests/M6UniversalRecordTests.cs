using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Models;
using TelemetryDashboard.Core.Records;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Verifies the M6 universal record layer: that a non-telemetry domain reaches the numeric
/// machinery through projection, and that everything else is declined rather than coerced.
/// </summary>
public class M6UniversalRecordTests
{
    [Fact]
    public void NumericRecord_RoundTripsThroughTelemetryPacket_WithoutLoss()
    {
        var original = new DataRecord
        {
            Key = new DataKey("NODE-7", "bus_voltage"),
            Timestamp = new DateTimeOffset(2026, 8, 19, 4, 30, 12, TimeSpan.Zero),
            Value = new DataValue.Numeric(-273.15, "V"),
            Source = "NODE-7",
            RawSource = "$VB,-273.150*7F"
        };

        TelemetryPacketProjection.TryToPacket(original, out TelemetryPacket packet).Should().BeTrue();
        DataRecord restored = TelemetryPacketProjection.ToRecord(packet);

        restored.Key.Should().Be(original.Key);
        restored.Timestamp.Should().Be(original.Timestamp);
        restored.Value.Should().Be(original.Value);
        restored.RawSource.Should().Be(original.RawSource);
        restored.IsDerived.Should().BeFalse();
    }

    [Fact]
    public void DerivedRecord_KeepsItsProvenanceAcrossTheProjection()
    {
        DataRecord derived = DataRecord.Derived(
            "clinic.ward3", "appt.4417#waitMinutes", new DataValue.Numeric(12.5, "min"), "wait-time");

        TelemetryPacketProjection.TryToPacket(derived, out TelemetryPacket packet).Should().BeTrue();
        packet.Flags.HasFlag(PacketFlags.IsDerived).Should().BeTrue();

        // The flag survives, but the producing projection's name does not fit in a TelemetryPacket.
        // The round trip therefore reports the record as derived-by-unknown rather than measured —
        // degrading to "derived, producer unrecoverable" is honest; degrading to "measured" is not.
        TelemetryPacketProjection.ToRecord(packet).DerivedFrom
            .Should().Be(TelemetryPacketProjection.UnnamedProjection);
    }

    [Fact]
    public void NonNumericRecord_IsRefused_NotCoercedToZero()
    {
        var appointment = new DataRecord
        {
            Key = new DataKey("clinic.ward3", "appt.4417"),
            Timestamp = DateTimeOffset.UtcNow,
            Value = new DataValue.Instant(new DateTimeOffset(2026, 9, 2, 14, 30, 0, TimeSpan.Zero))
        };

        TelemetryPacketProjection.TryToPacket(appointment, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Pipeline_CountsWhatEachStageDeclined()
    {
        var numeric = new NumericPacketStage("scope", _ => { });
        var pipeline = new RecordPipeline().Register(numeric);

        await pipeline.DispatchAsync(DataRecord.Measured("plant", "temp", 21.0, "C"));
        await pipeline.DispatchAsync(new DataRecord
        {
            Key = new DataKey("plant", "state"),
            Timestamp = DateTimeOffset.UtcNow,
            Value = new DataValue.Text("RUNNING")
        });

        StageActivity activity = pipeline.Activity().Single();
        activity.Accepted.Should().Be(1);
        activity.Declined.Should().Be(1);
        activity.Offered.Should().Be(2);
    }

    /// <summary>
    /// The generalisation's whole purpose: a clinic's appointment book, which carries no numbers at
    /// all, is scored by the same engine that watches a power converter — via a projection, with no
    /// change to the engine and no invented values.
    /// </summary>
    [Fact]
    public async Task AppointmentStream_GainsRealAnomalyDetection_ThroughDerivedNumericProjection()
    {
        var engine = new TelemetryMlAnalyticsEngine(windowSize: 60, sampleRateHz: 1.0);
        var scored = new List<AnomalyResult>();

        var pipeline = new RecordPipeline();
        pipeline.Register(new NumericPacketStage(
            "analytics",
            packet => scored.Add(engine.AnalyzeChannel(packet.Variable, packet.Value))));

        // Waiting time is the appointment's scheduled instant against when the patient was seen.
        var waitTime = new DerivedNumericProjection(
            name: "wait-time",
            accepts: DataValueKind.Instant,
            measure: r => r.Value is DataValue.Instant scheduled
                ? (r.Timestamp - scheduled.Value).TotalMinutes
                : null,
            keySuffix: "#waitMinutes",
            unit: "min",
            // Emitting back into the same pipeline is safe: the derived record is Numeric, which
            // this projection declines, so it reaches the analytics stage and stops.
            emit: async (derived, token) => await pipeline.DispatchAsync(derived, token));

        pipeline.Register(waitTime);

        var start = new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < 50; i++)
        {
            DateTimeOffset booked = start.AddMinutes(i * 10);
            await pipeline.DispatchAsync(Appointment(booked, seenAfterMinutes: 5 + (i % 3)));
        }

        // One patient waits an hour and a half.
        await pipeline.DispatchAsync(Appointment(start.AddMinutes(510), seenAfterMinutes: 90));

        AnomalyResult worst = scored[^1];
        worst.ChannelName.Should().Be("appt#waitMinutes");
        worst.HasVerdict.Should().BeTrue("the engine had a full baseline by then");
        worst.ZScore.Should().BeGreaterThan(3.0);
        worst.IsAnomaly.Should().BeTrue();

        // Nothing was invented: every scored figure came from a projection that named itself.
        scored.Should().HaveCount(51);
        waitTime.UnmeasurableCount.Should().Be(0);
    }

    private static DataRecord Appointment(DateTimeOffset booked, double seenAfterMinutes) => new()
    {
        Key = new DataKey("clinic.ward3", "appt"),
        Timestamp = booked.AddMinutes(seenAfterMinutes),
        Value = new DataValue.Instant(booked),
        Source = "clinic-booking-system"
    };
}
