using System;
using System.Text;
using FluentAssertions;
using TelemetryDashboard.Core.Services;
using Xunit;
using TelemetryDashboard.Core.Security;

namespace TelemetryDashboard.Tests.Tiers.Tier1_FeatureCoverage;

public class F04_AesSecurityProviderTests
{
    [Fact]
    [Trait("Category", "Tier1")]
    public void AesGcm_EncryptAndDecrypt_RoundTripSuccess()
    {
        var provider = new AesSecurityProvider();
        byte[] key = Encoding.UTF8.GetBytes("SuperSecretEnterpriseKey2026!");
        byte[] plainData = Encoding.UTF8.GetBytes("TelemetryPacket:NODE1,TEMP=42.5");

        byte[] encrypted = provider.EncryptPayload(plainData, key);
        encrypted.Should().NotBeNullOrEmpty();
        encrypted.Length.Should().BeGreaterThan(28); // 12 nonce + 16 tag + payload

        byte[] decrypted = provider.DecryptPayload(encrypted, key);
        Encoding.UTF8.GetString(decrypted).Should().Be("TelemetryPacket:NODE1,TEMP=42.5");
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void AesGcm_TamperedCiphertext_ReturnsEmptyBytes()
    {
        var provider = new AesSecurityProvider();
        byte[] key = Encoding.UTF8.GetBytes("SecurityKey123");
        byte[] plainData = Encoding.UTF8.GetBytes("SensitiveDataPayload");

        byte[] encrypted = provider.EncryptPayload(plainData, key);
        // Tamper with payload byte
        encrypted[^1] ^= 0xFF;

        byte[] decrypted = provider.DecryptPayload(encrypted, key);
        decrypted.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Ed25519_SignAndVerify_ValidSignaturePasses()
    {
        var provider = new AesSecurityProvider();
        byte[] seed = new byte[32];
        new Random(42).NextBytes(seed);
        // Verification must use the public half of the key pair. Passing the seed itself
        // is what the old forgery fallback accepted, and it is precisely what must not work.
        byte[] publicKey = AesSecurityProvider.DerivePublicKey(seed);

        byte[] message = Encoding.UTF8.GetBytes("CriticalMcuCommand:ABORT");

        byte[] signature = provider.SignData(message, seed);
        signature.Should().HaveCount(64);

        bool isValid = provider.VerifySignature(message, signature, publicKey);
        isValid.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void Ed25519_TamperedMessage_SignatureFails()
    {
        var provider = new AesSecurityProvider();
        byte[] seed = new byte[32];
        new Random(100).NextBytes(seed);
        byte[] publicKey = AesSecurityProvider.DerivePublicKey(seed);

        byte[] message = Encoding.UTF8.GetBytes("OriginalMessage");
        byte[] signature = provider.SignData(message, seed);

        byte[] tamperedMessage = Encoding.UTF8.GetBytes("TamperedMessage");
        bool isValid = provider.VerifySignature(tamperedMessage, signature, publicKey);
        isValid.Should().BeFalse();
    }
}
