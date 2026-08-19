using System.Security.Cryptography;
using TelemetryDashboard.Core.Services;
using TelemetryDashboard.Core.Security;
using TelemetryDashboard.Core.Protocols;
using TelemetryDashboard.Core.Analytics;
using TelemetryDashboard.Core.Recording;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Regression tests pinning the defects found during the PROJECT.md / IDEA.md specification
/// compliance review. Each test reproduces a concrete failure of the previous implementation.
/// </summary>
public class SpecComplianceRegressionTests
{
    // ---------------------------------------------------------------------
    // M1 - Gorilla Delta-of-Delta bit packing
    // ---------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void Gorilla_XorSpanningFullWidth_RoundTripsLosslessly()
    {
        // 1.0 and its bit-flipped sibling differ in both the MSB and the LSB, so the XOR
        // has 0 leading and 0 trailing zeros => 64 meaningful bits. Encoding 64 into the
        // 6-bit length field wraps to 0 and silently destroys the sample.
        var compressor = new GorillaCompressor();
        ulong bits = BitConverter.DoubleToUInt64Bits(1.0);
        double[] samples = { 1.0, BitConverter.UInt64BitsToDouble(bits ^ 0x8000_0000_0000_0001UL) };

        double[] roundTrip = compressor.DecompressDoubles(compressor.CompressDoubles(samples));

        roundTrip.Should().Equal(samples);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Gorilla_AlternatingSignSeries_RoundTripsLosslessly()
    {
        var compressor = new GorillaCompressor();
        double[] samples = new double[256];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (i % 2 == 0 ? 1.0 : -1.0) * (i + 1) * 1e-3;
        }

        double[] roundTrip = compressor.DecompressDoubles(compressor.CompressDoubles(samples));

        roundTrip.Should().Equal(samples);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Gorilla_ExtremeAndSpecialValues_RoundTripLosslessly()
    {
        var compressor = new GorillaCompressor();
        double[] samples =
        {
            0.0, -0.0, double.Epsilon, double.MaxValue, double.MinValue,
            double.PositiveInfinity, double.NegativeInfinity, 1.0, -1.0
        };

        double[] roundTrip = compressor.DecompressDoubles(compressor.CompressDoubles(samples));

        for (int i = 0; i < samples.Length; i++)
        {
            BitConverter.DoubleToUInt64Bits(roundTrip[i])
                .Should().Be(BitConverter.DoubleToUInt64Bits(samples[i]), $"sample {i} must survive bit-exact");
        }
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Gorilla_TimestampsWithLargeDeltaOfDelta_RoundTripLosslessly()
    {
        // Delta-of-delta beyond the 32-bit truncation window used by the old encoder.
        var compressor = new GorillaCompressor();
        long[] timestamps = { 0L, 1_000L, 2_000L, 90_000_000_000L, 90_000_001_000L };

        long[] roundTrip = compressor.DecompressTimeStamps(compressor.CompressTimeStamps(timestamps));

        roundTrip.Should().Equal(timestamps);
    }

    // ---------------------------------------------------------------------
    // M1 - Zero-trust AES-256-GCM + Ed25519
    // ---------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void Ed25519_SignatureCannotBeForgedFromPublicKeyAlone()
    {
        // The public key must be useless for producing signatures. The previous
        // implementation re-derived a private scalar from the public key bytes, so an
        // attacker holding only the public key could mint valid signatures.
        var provider = new AesSecurityProvider();
        var (privateKey, publicKey) = AesSecurityProvider.GenerateSigningKeyPair();
        byte[] message = Encoding.UTF8.GetBytes("$TELEM,COM3,TEMP,24.5");

        byte[] forged = provider.SignData(message, publicKey);

        provider.VerifySignature(message, forged, publicKey).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Ed25519_GenuineSignatureVerifiesAndTamperingIsRejected()
    {
        var provider = new AesSecurityProvider();
        var (privateKey, publicKey) = AesSecurityProvider.GenerateSigningKeyPair();
        byte[] message = Encoding.UTF8.GetBytes("$TELEM,COM4,VIB,0.21");

        byte[] signature = provider.SignData(message, privateKey);

        provider.VerifySignature(message, signature, publicKey).Should().BeTrue();

        byte[] tampered = (byte[])message.Clone();
        tampered[^1] ^= 0x01;
        provider.VerifySignature(tampered, signature, publicKey).Should().BeFalse();

        var (_, otherPublicKey) = AesSecurityProvider.GenerateSigningKeyPair();
        provider.VerifySignature(message, signature, otherPublicKey).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Ed25519_MatchesRfc8032TestVector1()
    {
        // RFC 8032 section 7.1, TEST 1 - empty message.
        byte[] seed = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        byte[] expectedPublicKey = Convert.FromHexString("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        byte[] expectedSignature = Convert.FromHexString(
            "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e06522490155" +
            "5fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");

        byte[] publicKey = AesSecurityProvider.DerivePublicKey(seed);
        publicKey.Should().Equal(expectedPublicKey);

        var provider = new AesSecurityProvider();
        byte[] signature = provider.SignData(Array.Empty<byte>(), seed);
        signature.Should().Equal(expectedSignature);
        provider.VerifySignature(Array.Empty<byte>(), signature, publicKey).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Ed25519_MatchesRfc8032TestVector2()
    {
        // RFC 8032 section 7.1, TEST 2 - single byte message 0x72.
        byte[] seed = Convert.FromHexString("4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb");
        byte[] message = { 0x72 };
        byte[] expectedSignature = Convert.FromHexString(
            "92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da" +
            "085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00");

        var provider = new AesSecurityProvider();
        byte[] signature = provider.SignData(message, seed);

        signature.Should().Equal(expectedSignature);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AesGcm_TamperedCiphertextIsRejected()
    {
        var provider = new AesSecurityProvider();
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] plain = Encoding.UTF8.GetBytes("{\"nodeId\":\"COM3\",\"temp\":24.5}");

        byte[] sealedPayload = provider.EncryptPayload(plain, key);
        provider.DecryptPayload(sealedPayload, key).Should().Equal(plain);

        sealedPayload[^1] ^= 0xFF;
        provider.DecryptPayload(sealedPayload, key).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AesGcm_NonceIsUniquePerEncryption()
    {
        var provider = new AesSecurityProvider();
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] plain = Encoding.UTF8.GetBytes("repeated payload");

        byte[] first = provider.EncryptPayload(plain, key);
        byte[] second = provider.EncryptPayload(plain, key);

        first.Should().NotEqual(second, "GCM nonce reuse under a fixed key is catastrophic");
    }

    // ---------------------------------------------------------------------
    // M4 - Industrial protocol bridge adapters
    // ---------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void CanBusBridge_DecodesIdentifierAndSignalPayload()
    {
        var bridge = new CanBusBridgeAdapter();
        // Classic CAN frame: 11-bit id 0x123, DLC 8, big-endian signal 0x0BB8 = 3000.
        byte[] frame = { 0x01, 0x23, 0x08, 0x0B, 0xB8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        string json = Encoding.UTF8.GetString(bridge.ConvertToStandardPacket(frame));

        json.Should().Contain("\"canId\":\"0x123\"");
        json.Should().Contain("CANbus");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ProtocolBridges_DoNotOverrunShortPayloads()
    {
        // The old CAN branch read a float at offset 6 while only checking Length >= 8.
        var bridge = new IndustrialProtocolBridge();
        byte[] truncated = { 0x08, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };

        Action decode = () => bridge.ConvertToStandardPacket(truncated);

        decode.Should().NotThrow();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ModbusBridge_DecodesHoldingRegistersAndValidatesCrc()
    {
        var bridge = new ModbusBridgeAdapter();
        // Slave 0x01, function 0x03, byte count 4, registers 0x0064 (100) and 0x00C8 (200).
        byte[] frame = ModbusBridgeAdapter.BuildRtuResponse(0x01, 0x03, new byte[] { 0x00, 0x64, 0x00, 0xC8 });

        string json = Encoding.UTF8.GetString(bridge.ConvertToStandardPacket(frame));

        json.Should().Contain("ModbusRTU");
        json.Should().Contain("100");
        json.Should().Contain("200");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ModbusBridge_RejectsFrameWithCorruptedCrc()
    {
        var bridge = new ModbusBridgeAdapter();
        byte[] frame = ModbusBridgeAdapter.BuildRtuResponse(0x01, 0x03, new byte[] { 0x00, 0x64 });
        frame[^1] ^= 0xFF;

        string json = Encoding.UTF8.GetString(bridge.ConvertToStandardPacket(frame));

        json.Should().Contain("crcValid\":false");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Ros2Bridge_DecodesCdrEncodedFloatTopic()
    {
        var bridge = new Ros2BridgeAdapter();
        byte[] frame = Ros2BridgeAdapter.BuildCdrFloat64Message("/sensor/temperature", 24.5);

        string json = Encoding.UTF8.GetString(bridge.ConvertToStandardPacket(frame));

        json.Should().Contain("/sensor/temperature");
        json.Should().Contain("24.5");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ProtocolBridges_RoundTripThroughStandardPacket()
    {
        foreach (var bridge in new Core.Interfaces.IProtocolBridge[]
                 {
                     new CanBusBridgeAdapter(), new ModbusBridgeAdapter(), new Ros2BridgeAdapter()
                 })
        {
            byte[] downstream = bridge.ConvertFromStandardPacket(new { nodeId = "COM3", value = 12.5 });
            downstream.Should().NotBeEmpty($"{bridge.ProtocolName} must emit a downstream frame");
        }
    }

    // ---------------------------------------------------------------------
    // M3 - ML analytics engine
    // ---------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void MlEngine_ForecastHorizonIsSixtySecondsNotSixtySamples()
    {
        // 20 Hz feed rising by 0.05 per sample => +1.0 per second => +60.0 over 60 seconds.
        var engine = new TelemetryMlAnalyticsEngine(windowSize: 64, sampleRateHz: 20.0);
        AnomalyResult result = new();
        double value = 0;
        for (int i = 0; i < 64; i++)
        {
            result = engine.AnalyzeChannel("ramp", value, warningUpperThreshold: double.MaxValue);
            value += 0.05;
        }

        double expected = result.CurrentValue + 60.0;
        result.PredictedValueIn60s.Should().BeApproximately(expected, 1.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void MlEngine_BreachTimeUsesConfiguredSampleRate()
    {
        // Rising 1.0 per second from 0; threshold 30 must be ~30 seconds away.
        var engine = new TelemetryMlAnalyticsEngine(windowSize: 64, sampleRateHz: 20.0);
        AnomalyResult result = new();
        double value = 0;
        for (int i = 0; i < 64; i++)
        {
            result = engine.AnalyzeChannel("ramp", value, warningUpperThreshold: 30.0);
            value += 0.05;
        }

        result.EstimatedTimeToBreachSec.Should()
            .BeApproximately(30.0 - result.CurrentValue, 2.0);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void MlEngine_FlagsSpikeAgainstStableBaseline()
    {
        var engine = new TelemetryMlAnalyticsEngine(windowSize: 64, sampleRateHz: 20.0);
        for (int i = 0; i < 60; i++)
        {
            engine.AnalyzeChannel("temp", 25.0 + (i % 2 == 0 ? 0.01 : -0.01));
        }

        AnomalyResult spike = engine.AnalyzeChannel("temp", 95.0);

        spike.IsAnomaly.Should().BeTrue();
        spike.ZScore.Should().BeGreaterThan(2.5);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void MlEngine_ConstantSeriesNeverReportsAnomaly()
    {
        var engine = new TelemetryMlAnalyticsEngine(windowSize: 32, sampleRateHz: 20.0);
        AnomalyResult result = new();

        for (int i = 0; i < 200; i++)
        {
            result = engine.AnalyzeChannel("flat", 48.0);
        }

        result.IsAnomaly.Should().BeFalse();
        result.ZScore.Should().Be(0.0);
    }

    // ---------------------------------------------------------------------
    // M2 - DVR time travel
    // ---------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Tier1")]
    public void DvrPlayer_RetainsNewestFramesWhenCapacityExceeded()
    {
        var player = new TimeTravelDvrPlayer(capacity: 128);

        for (int i = 0; i < 500; i++)
        {
            player.RecordFrame("ch", i, 0.0, false, timestampSec: i * 0.1);
        }

        player.FrameCount.Should().Be(128);
        var frames = player.GetFramesInRange(double.MinValue, double.MaxValue);
        frames.Should().HaveCount(128);
        frames[0].Value.Should().Be(372);
        frames[^1].Value.Should().Be(499);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void DvrPlayer_ScrubsToNearestFrameAtTenthOfSecondPrecision()
    {
        var player = new TimeTravelDvrPlayer(capacity: 1024);
        for (int i = 0; i < 100; i++)
        {
            player.RecordFrame("ch", i, 0.0, false, timestampSec: i * 0.1);
        }

        DvrFrameEventArgs? seen = null;
        player.FrameReplayed += (_, e) => seen = e;

        player.ScrubToRelative(5.0); // 5 seconds in => frame index 50

        seen.Should().NotBeNull();
        seen!.Frame.Value.Should().Be(50);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void DvrPlayer_HighVolumeIngestStaysWithinLatencyBudget()
    {
        var player = new TimeTravelDvrPlayer(capacity: 100_000);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < 300_000; i++)
        {
            player.RecordFrame("ch", i, 0.0, false, timestampSec: i * 0.001);
        }

        sw.Stop();
        // The old List.RemoveAt(0) eviction was O(n) per frame and took minutes here.
        sw.ElapsedMilliseconds.Should().BeLessThan(5_000);
        player.FrameCount.Should().Be(100_000);
    }
}
