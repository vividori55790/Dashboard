using System.Text;
using System.Text.Json;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Security;

namespace TelemetryDashboard.Tests;

/// <summary>
/// Verifies that mesh frames are confidential, authenticated, and replay-resistant.
/// </summary>
public class MeshSecurityTests
{
    private static readonly byte[] ClusterKey = MeshPacketCodec.DeriveClusterKey("factory-a-secret");

    private static MeshSyncPacket SamplePacket() => new()
    {
        SourcePeerId = "peer-a",
        HubName = "Factory-A",
        PacketType = "ANOMALY",
        TimestampSec = 1_700_000_000,
        PayloadJson = "{\"zScore\":3.9}"
    };

    [Fact]
    [Trait("Category", "Tier1")]
    public void SecuredFrame_RoundTripsBetweenHubsSharingTheClusterKey()
    {
        var sender = new MeshPacketCodec(ClusterKey);
        var receiver = new MeshPacketCodec(ClusterKey);

        byte[] frame = sender.Encode(SamplePacket());

        receiver.TryDecode(frame, out MeshSyncPacket? decoded, out byte[]? senderKey).Should().BeTrue();
        decoded!.HubName.Should().Be("Factory-A");
        decoded.PayloadJson.Should().Contain("3.9");
        senderKey.Should().Equal(sender.PublicKey);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void SecuredFrame_DoesNotExposePayloadOnTheWire()
    {
        var sender = new MeshPacketCodec(ClusterKey);

        byte[] frame = sender.Encode(SamplePacket());
        string onWire = Encoding.UTF8.GetString(frame);

        onWire.Should().NotContain("Factory-A");
        onWire.Should().NotContain("zScore");
        sender.Mode.Should().Be(MeshSecurityMode.Encrypted);
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ForgedFrameFromAnotherClusterIsRejected()
    {
        var outsider = new MeshPacketCodec(MeshPacketCodec.DeriveClusterKey("attacker-guess"));
        var receiver = new MeshPacketCodec(ClusterKey);

        byte[] forged = outsider.Encode(SamplePacket());

        receiver.TryDecode(forged, out MeshSyncPacket? decoded, out _).Should().BeFalse();
        decoded.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void PlaintextInjectionIsRejectedBySecuredHub()
    {
        var receiver = new MeshPacketCodec(ClusterKey);

        // Exactly what the old mesh accepted from anyone on the segment.
        byte[] injected = JsonSerializer.SerializeToUtf8Bytes(SamplePacket());

        receiver.TryDecode(injected, out MeshSyncPacket? decoded, out _).Should().BeFalse();
        decoded.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void TamperedCiphertextIsRejected()
    {
        var sender = new MeshPacketCodec(ClusterKey);
        var receiver = new MeshPacketCodec(ClusterKey);

        byte[] frame = sender.Encode(SamplePacket());
        frame[^1] ^= 0xFF;

        receiver.TryDecode(frame, out MeshSyncPacket? decoded, out _).Should().BeFalse();
        decoded.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ReplayedFrameIsAcceptedOnlyOnce()
    {
        var sender = new MeshPacketCodec(ClusterKey);
        var receiver = new MeshPacketCodec(ClusterKey);

        byte[] frame = sender.Encode(SamplePacket());

        receiver.TryDecode(frame, out MeshSyncPacket? first, out _).Should().BeTrue();
        first.Should().NotBeNull();

        // A captured datagram replayed later must not re-enter the cluster.
        receiver.TryDecode(frame, out MeshSyncPacket? second, out _).Should().BeFalse();
        second.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void UnsecuredModeIsReportedRatherThanImplied()
    {
        var open = new MeshPacketCodec();

        open.Mode.Should().Be(MeshSecurityMode.Unsecured);
        open.PublicKey.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Tier1")]
    public void ClusterKeyDerivationIsDeterministicAndClusterScoped()
    {
        MeshPacketCodec.DeriveClusterKey("shared", "plant-1")
            .Should().Equal(MeshPacketCodec.DeriveClusterKey("shared", "plant-1"));

        MeshPacketCodec.DeriveClusterKey("shared", "plant-1")
            .Should().NotEqual(MeshPacketCodec.DeriveClusterKey("shared", "plant-2"));
    }
}
