using System;
using System.Buffers.Binary;
using System.IO;
using TelemetryDashboard.Core.Services;

namespace TelemetryDashboard.Core.Storage;

/// <summary>
/// Packs a run of samples from one channel into a single self-describing compressed block.
/// </summary>
/// <remarks>
/// The compression is the Gorilla scheme already implemented in <see cref="GorillaCompressor"/> on
/// top of <c>BitWriter</c>/<c>BitReader</c>: delta-of-delta for the timestamps, XOR against the
/// previous sample for the IEEE-754 value bits. This type adds only what storing a block needs —
/// a header saying which codec produced it and where the streams meet — rather than a second copy
/// of the codec.
/// <para>
/// Three streams: timestamps, values, and the per-sample flag word, which is delta-of-delta coded
/// too because it almost never changes and so costs about a bit per sample. When every flag is
/// zero the stream is omitted entirely. Flags are carried rather than dropped because
/// <c>ChecksumFailed</c> on a sample is part of what that sample means.
/// </para>
/// <para>
/// The value path is bit-exact: it never inspects what a pattern means, so NaN (including a
/// non-default payload), the infinities and negative zero come back exactly as they went in. That
/// matters more than usual here, because a NaN in this system marks "no reading" — a codec that
/// normalised it would turn a gap into a measurement.
/// </para>
/// </remarks>
public static class GorillaBlockCodec
{
    private static readonly GorillaCompressor Codec = new();

    /// <summary>Marks a block as Gorilla-encoded, so a foreign blob fails loudly rather than decoding to noise.</summary>
    private const byte Magic = 0x47; // 'G'

    /// <summary>Wire format version, stored in every block.</summary>
    public const byte Version = 1;

    /// <summary>Codec identifier written to the <c>codec</c> column beside a block.</summary>
    public const int CodecId = 1;

    private const int HeaderBytes = 14;

    /// <summary>
    /// Encodes <paramref name="values"/> stamped at <paramref name="timestampTicks"/> with
    /// <paramref name="flags"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The arrays differ in length, or are empty.</exception>
    public static byte[] Encode(long[] timestampTicks, double[] values, long[] flags)
    {
        ArgumentNullException.ThrowIfNull(timestampTicks);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(flags);

        if (timestampTicks.Length != values.Length || timestampTicks.Length != flags.Length)
        {
            throw new ArgumentException(
                $"Stream lengths differ ({timestampTicks.Length}/{values.Length}/{flags.Length}); " +
                "a block whose streams disagree cannot be decoded back into samples.", nameof(values));
        }

        if (timestampTicks.Length == 0)
        {
            throw new ArgumentException("A block must contain at least one sample.", nameof(timestampTicks));
        }

        byte[] stamps = Codec.CompressTimeStamps(timestampTicks);
        byte[] payload = Codec.CompressDoubles(values);
        byte[] marks = Array.TrueForAll(flags, f => f == 0) ? Array.Empty<byte>() : Codec.CompressTimeStamps(flags);

        var block = new byte[HeaderBytes + stamps.Length + payload.Length + marks.Length];
        block[0] = Magic;
        block[1] = Version;
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(2), timestampTicks.Length);
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(6), stamps.Length);
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(10), payload.Length);
        stamps.CopyTo(block, HeaderBytes);
        payload.CopyTo(block, HeaderBytes + stamps.Length);
        marks.CopyTo(block, HeaderBytes + stamps.Length + payload.Length);
        return block;
    }

    /// <summary>
    /// Decodes a block back into the exact samples that were encoded.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The blob is truncated, is not a Gorilla block, was written by a newer format version, or
    /// decodes to a different number of samples than its header claims.
    /// </exception>
    public static (long[] Ticks, double[] Values, long[] Flags) Decode(byte[] block)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (block.Length < HeaderBytes || block[0] != Magic)
        {
            throw new InvalidDataException("Not a Gorilla block: header magic missing or blob truncated.");
        }

        if (block[1] != Version)
        {
            throw new InvalidDataException(
                $"Gorilla block version {block[1]} was written by a newer store; this build reads version {Version}.");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(2));
        int stampBytes = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(6));
        int valueBytes = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(10));
        int valueStart = HeaderBytes + stampBytes;
        int flagStart = valueStart + valueBytes;

        if (count <= 0 || stampBytes < 0 || valueBytes < 0 || flagStart > block.Length)
        {
            throw new InvalidDataException("Gorilla block header describes a layout the blob cannot hold.");
        }

        long[] ticks = Codec.DecompressTimeStamps(block[HeaderBytes..valueStart]);
        double[] values = Codec.DecompressDoubles(block[valueStart..flagStart]);
        long[] flags = flagStart == block.Length
            ? new long[count]
            : Codec.DecompressTimeStamps(block[flagStart..]);

        if (ticks.Length != count || values.Length != count || flags.Length != count)
        {
            throw new InvalidDataException(
                $"Gorilla block claims {count} samples but decoded {ticks.Length} timestamps, " +
                $"{values.Length} values and {flags.Length} flag words.");
        }

        return (ticks, values, flags);
    }
}
