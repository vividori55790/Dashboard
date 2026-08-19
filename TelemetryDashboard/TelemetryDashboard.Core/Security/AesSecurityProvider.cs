using System;
using System.Security.Cryptography;
using TelemetryDashboard.Core.Interfaces;
using TelemetryDashboard.Core.Security;

namespace TelemetryDashboard.Core.Security;

/// <summary>
/// Zero-trust telemetry security provider: AES-256-GCM authenticated encryption for payloads
/// and Ed25519 digital signatures for edge-device authentication.
/// </summary>
/// <remarks>
/// Signature verification takes the <em>public</em> key only. Callers must obtain a key pair from
/// <see cref="GenerateSigningKeyPair"/> (or derive the public half with <see cref="DerivePublicKey"/>)
/// and distribute only the public key to verifiers.
/// </remarks>
public class AesSecurityProvider : ISecurityProvider
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = NonceSize + TagSize;

    /// <summary>Generates a new Ed25519 signing key pair.</summary>
    public static (byte[] PrivateKey, byte[] PublicKey) GenerateSigningKeyPair() => Ed25519.GenerateKeyPair();

    /// <summary>Derives the Ed25519 public key that corresponds to a private seed.</summary>
    public static byte[] DerivePublicKey(byte[] privateSeed) => Ed25519.DerivePublicKey(NormalizeSeed(privateSeed));

    /// <summary>Generates a full-entropy 256-bit AES key.</summary>
    public static byte[] GenerateEncryptionKey() => RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Encrypts with AES-256-GCM. Wire format: [ nonce (12) | tag (16) | ciphertext (n) ].
    /// A fresh random nonce is drawn per call, so identical plaintexts never produce identical output.
    /// </summary>
    public byte[] EncryptPayload(byte[] plainData, byte[] key)
    {
        if (plainData == null || plainData.Length == 0) return Array.Empty<byte>();

        byte[] aesKey = NormalizeEncryptionKey(key);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[plainData.Length];

        using (var aes = new AesGcm(aesKey, TagSize))
        {
            aes.Encrypt(nonce, plainData, ciphertext, tag);
        }

        var payload = new byte[HeaderSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, payload, HeaderSize, ciphertext.Length);
        return payload;
    }

    /// <summary>
    /// Decrypts and authenticates an AES-256-GCM payload.
    /// Returns an empty array when the tag does not validate, i.e. the packet was tampered with.
    /// </summary>
    public byte[] DecryptPayload(byte[] encryptedData, byte[] key)
    {
        if (encryptedData == null || encryptedData.Length < HeaderSize) return Array.Empty<byte>();

        byte[] aesKey = NormalizeEncryptionKey(key);
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertext = new byte[encryptedData.Length - HeaderSize];

        Buffer.BlockCopy(encryptedData, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(encryptedData, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(encryptedData, HeaderSize, ciphertext, 0, ciphertext.Length);

        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(aesKey, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException)
        {
            return Array.Empty<byte>();
        }
    }

    /// <summary>Signs data with an Ed25519 private seed, producing a 64-byte signature.</summary>
    public byte[] SignData(byte[] data, byte[] privateKey) =>
        Ed25519.Sign(data ?? Array.Empty<byte>(), NormalizeSeed(privateKey));

    /// <summary>
    /// Verifies a signature against the signer's <em>public</em> key.
    /// Possession of the public key alone never yields a valid signature.
    /// </summary>
    public bool VerifySignature(byte[] data, byte[] signature, byte[] publicKey)
    {
        if (signature is not { Length: Ed25519.SignatureSize }) return false;
        if (publicKey is not { Length: Ed25519.PublicKeySize }) return false;

        return Ed25519.Verify(data ?? Array.Empty<byte>(), signature, publicKey);
    }

    /// <summary>
    /// Produces a 256-bit AES key. A 32-byte key is used verbatim; any other length is hashed
    /// to full width. The previous implementation zero-padded short keys, which left most of
    /// the key space unused and made a short passphrase far weaker than its length suggested.
    /// </summary>
    private static byte[] NormalizeEncryptionKey(byte[] key)
    {
        if (key is { Length: 32 }) return key;
        return SHA256.HashData(key ?? Array.Empty<byte>());
    }

    /// <summary>
    /// Produces a 32-byte Ed25519 seed. Signing and public-key derivation share this routine so
    /// that a caller-supplied key of any length maps to one consistent key pair.
    /// </summary>
    private static byte[] NormalizeSeed(byte[] seed)
    {
        if (seed is { Length: Ed25519.SeedSize }) return seed;
        return SHA256.HashData(seed ?? Array.Empty<byte>());
    }
}
