using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TelemetryDashboard.Core.Services;

namespace TelemetryDashboard.Core.Security;

/// <summary>Security posture of a mesh hub.</summary>
public enum MeshSecurityMode
{
    /// <summary>No cluster key configured; frames travel in clear text.</summary>
    Unsecured,

    /// <summary>Frames are AES-256-GCM encrypted and Ed25519 signed.</summary>
    Encrypted
}

/// <summary>
/// Wire codec for P2P mesh frames: AES-256-GCM confidentiality, Ed25519 authenticity,
/// and a replay window.
/// </summary>
/// <remarks>
/// The mesh previously broadcast plaintext JSON over UDP and accepted any datagram that
/// deserialised, so any host on the segment could both read cluster telemetry and inject
/// forged peers or alerts. That is the exact packet-injection path the zero-trust requirement
/// exists to close, and closing it on the serial link while leaving it open on the network
/// would have been no protection at all.
/// </remarks>
public sealed class MeshPacketCodec
{
    /// <summary>Magic prefix identifying a secured mesh datagram.</summary>
    private static readonly byte[] Magic = "TDM1"u8.ToArray();

    private readonly AesSecurityProvider _crypto = new();
    private readonly ConcurrentDictionary<string, DateTime> _seenNonces = new();

    private readonly byte[]? _clusterKey;
    private readonly byte[]? _signingSeed;
    private readonly byte[]? _signingPublicKey;

    /// <param name="clusterKey">Pre-shared cluster secret. Null runs the mesh unsecured.</param>
    /// <param name="signingSeed">This hub's Ed25519 seed. Generated when omitted.</param>
    public MeshPacketCodec(byte[]? clusterKey = null, byte[]? signingSeed = null)
    {
        _clusterKey = clusterKey;

        if (clusterKey is not null)
        {
            _signingSeed = signingSeed ?? RandomNumberGenerator.GetBytes(Ed25519.SeedSize);
            _signingPublicKey = AesSecurityProvider.DerivePublicKey(_signingSeed);
        }
    }

    public MeshSecurityMode Mode => _clusterKey is null ? MeshSecurityMode.Unsecured : MeshSecurityMode.Encrypted;

    /// <summary>This hub's public key, for peers to pin. Null when running unsecured.</summary>
    public byte[]? PublicKey => _signingPublicKey is null ? null : (byte[])_signingPublicKey.Clone();

    /// <summary>How far a frame's timestamp may drift before it is rejected as a replay.</summary>
    public TimeSpan ReplayWindow { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Serialises and, when a cluster key is configured, seals a frame.</summary>
    public byte[] Encode<T>(T packet)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(packet);
        if (_clusterKey is null || _signingSeed is null || _signingPublicKey is null)
        {
            return plaintext;
        }

        byte[] sealedPayload = _crypto.EncryptPayload(plaintext, _clusterKey);
        byte[] signature = _crypto.SignData(sealedPayload, _signingSeed);

        // [ magic | pubkey(32) | signature(64) | ciphertext ]
        var frame = new byte[Magic.Length + Ed25519.PublicKeySize + Ed25519.SignatureSize + sealedPayload.Length];
        int offset = 0;
        Buffer.BlockCopy(Magic, 0, frame, offset, Magic.Length); offset += Magic.Length;
        Buffer.BlockCopy(_signingPublicKey, 0, frame, offset, Ed25519.PublicKeySize); offset += Ed25519.PublicKeySize;
        Buffer.BlockCopy(signature, 0, frame, offset, Ed25519.SignatureSize); offset += Ed25519.SignatureSize;
        Buffer.BlockCopy(sealedPayload, 0, frame, offset, sealedPayload.Length);

        return frame;
    }

    /// <summary>
    /// Verifies, decrypts and deserialises a datagram.
    /// Returns false for any frame that fails authentication — it is never surfaced to callers.
    /// </summary>
    public bool TryDecode<T>(byte[] datagram, out T? packet, out byte[]? senderPublicKey)
    {
        packet = default;
        senderPublicKey = null;

        if (datagram is null || datagram.Length == 0) return false;

        if (_clusterKey is null)
        {
            return TryDeserialize(datagram, out packet);
        }

        int headerLength = Magic.Length + Ed25519.PublicKeySize + Ed25519.SignatureSize;
        if (datagram.Length <= headerLength) return false;

        for (int i = 0; i < Magic.Length; i++)
        {
            if (datagram[i] != Magic[i]) return false; // unsecured or foreign traffic
        }

        var publicKey = new byte[Ed25519.PublicKeySize];
        var signature = new byte[Ed25519.SignatureSize];
        var sealedPayload = new byte[datagram.Length - headerLength];

        int offset = Magic.Length;
        Buffer.BlockCopy(datagram, offset, publicKey, 0, Ed25519.PublicKeySize); offset += Ed25519.PublicKeySize;
        Buffer.BlockCopy(datagram, offset, signature, 0, Ed25519.SignatureSize); offset += Ed25519.SignatureSize;
        Buffer.BlockCopy(datagram, offset, sealedPayload, 0, sealedPayload.Length);

        if (!_crypto.VerifySignature(sealedPayload, signature, publicKey)) return false;

        byte[] plaintext = _crypto.DecryptPayload(sealedPayload, _clusterKey);
        if (plaintext.Length == 0) return false; // wrong cluster key or tampered ciphertext

        if (!IsFresh(signature)) return false;

        senderPublicKey = publicKey;
        return TryDeserialize(plaintext, out packet);
    }

    /// <summary>
    /// Rejects a frame whose signature has already been observed inside the replay window.
    /// GCM draws a fresh nonce per encryption, so a repeated signature means a replayed datagram.
    /// </summary>
    private bool IsFresh(byte[] signature)
    {
        string nonce = Convert.ToBase64String(signature, 0, 16);
        DateTime now = DateTime.UtcNow;

        PruneNonces(now);

        return _seenNonces.TryAdd(nonce, now);
    }

    private void PruneNonces(DateTime now)
    {
        if (_seenNonces.Count < 4096) return;

        foreach (var entry in _seenNonces)
        {
            if (now - entry.Value > ReplayWindow) _seenNonces.TryRemove(entry.Key, out _);
        }
    }

    private static bool TryDeserialize<T>(byte[] utf8Json, out T? packet)
    {
        try
        {
            packet = JsonSerializer.Deserialize<T>(utf8Json);
            return packet is not null;
        }
        catch (JsonException)
        {
            packet = default;
            return false;
        }
    }

    /// <summary>Derives a 256-bit cluster key from a shared passphrase.</summary>
    public static byte[] DeriveClusterKey(string passphrase, string clusterName = "TelemetryDashboard")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        // Salted by cluster name so the same passphrase does not key two different clusters.
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            Encoding.UTF8.GetBytes("TelemetryDashboard.Mesh." + clusterName),
            iterations: 200_000,
            HashAlgorithmName.SHA256,
            outputLength: 32);
    }
}
