using System;
using System.Numerics;
using TelemetryDashboard.Core.Collections;
using TelemetryDashboard.Core.Interfaces;

namespace TelemetryDashboard.Core.Services;

/// <summary>
/// Facebook Gorilla compressor: XOR bit packing for 64-bit floats and Delta-of-Delta
/// bit packing for timestamps.
/// </summary>
/// <remarks>
/// Value stream layout: 32-bit count, the first sample verbatim (64 bits), then per sample
/// <c>0</c> for an unchanged value, <c>10</c> to reuse the previous meaningful-bit window, or
/// <c>11</c> followed by a 5-bit leading-zero count and a 6-bit <em>length minus one</em> field.
/// Storing <c>length - 1</c> is what makes a full-width 64-bit XOR representable; encoding the
/// raw length wraps to zero in six bits and silently drops the sample.
/// </remarks>
public class GorillaCompressor : ITelemetryCompressor
{
    // A non-zero XOR has at most 63 combined leading+trailing zeros, so the meaningful window
    // is always 1..64 bits wide and 'length - 1' always fits in six bits.
    private const int MaxLeadingZeroBits = 31; // 5-bit field
    private const int LengthFieldBits = 6;

    public byte[] CompressFloatStream(double[] samples) => CompressDoubles(samples);

    public double[] DecompressFloatStream(byte[] compressedBytes) => DecompressDoubles(compressedBytes);

    public byte[] CompressDoubles(double[] samples)
    {
        if (samples == null || samples.Length == 0) return Array.Empty<byte>();

        var writer = new BitWriter(samples.Length + 16);
        writer.WriteBits((ulong)samples.Length, 32);

        ulong previousBits = BitConverter.DoubleToUInt64Bits(samples[0]);
        writer.WriteBits(previousBits, 64);

        int windowLeadingZeros = -1;
        int windowTrailingZeros = -1;

        for (int i = 1; i < samples.Length; i++)
        {
            ulong currentBits = BitConverter.DoubleToUInt64Bits(samples[i]);
            ulong xor = currentBits ^ previousBits;
            previousBits = currentBits;

            if (xor == 0)
            {
                writer.WriteBit(false); // '0' - identical to the previous sample
                continue;
            }

            writer.WriteBit(true);

            int leadingZeros = Math.Min(MaxLeadingZeroBits, BitOperations.LeadingZeroCount(xor));
            int trailingZeros = BitOperations.TrailingZeroCount(xor);

            bool fitsInPreviousWindow = windowLeadingZeros >= 0
                                        && leadingZeros >= windowLeadingZeros
                                        && trailingZeros >= windowTrailingZeros;

            if (fitsInPreviousWindow)
            {
                writer.WriteBit(false); // '10' - reuse the established window
                int windowLength = 64 - windowLeadingZeros - windowTrailingZeros;
                writer.WriteBits(xor >> windowTrailingZeros, windowLength);
            }
            else
            {
                writer.WriteBit(true); // '11' - declare a new window
                int length = 64 - leadingZeros - trailingZeros;

                writer.WriteBits((ulong)leadingZeros, 5);
                writer.WriteBits((ulong)(length - 1), LengthFieldBits);
                writer.WriteBits(xor >> trailingZeros, length);

                windowLeadingZeros = leadingZeros;
                windowTrailingZeros = trailingZeros;
            }
        }

        return writer.ToArray();
    }

    public double[] DecompressDoubles(byte[] compressedBytes)
    {
        if (compressedBytes == null || compressedBytes.Length < 4) return Array.Empty<double>();

        var reader = new BitReader(compressedBytes);
        int count = (int)reader.ReadBits(32);
        if (count <= 0) return Array.Empty<double>();

        var result = new double[count];
        ulong previousBits = reader.ReadBits(64);
        result[0] = BitConverter.UInt64BitsToDouble(previousBits);

        int windowLeadingZeros = -1;
        int windowTrailingZeros = -1;

        for (int i = 1; i < count; i++)
        {
            ulong xor = 0;

            if (reader.ReadBit())
            {
                if (reader.ReadBit())
                {
                    // '11' - a new meaningful-bit window follows.
                    int leadingZeros = (int)reader.ReadBits(5);
                    int length = (int)reader.ReadBits(LengthFieldBits) + 1;
                    int trailingZeros = 64 - leadingZeros - length;

                    if (trailingZeros < 0)
                    {
                        throw new InvalidDataException("Corrupted Gorilla stream: meaningful-bit window exceeds 64 bits.");
                    }

                    xor = reader.ReadBits(length) << trailingZeros;
                    windowLeadingZeros = leadingZeros;
                    windowTrailingZeros = trailingZeros;
                }
                else
                {
                    // '10' - reuse the window established by the previous control block.
                    if (windowLeadingZeros < 0)
                    {
                        throw new InvalidDataException("Corrupted Gorilla stream: window reuse before any window was declared.");
                    }

                    int windowLength = 64 - windowLeadingZeros - windowTrailingZeros;
                    xor = reader.ReadBits(windowLength) << windowTrailingZeros;
                }
            }

            previousBits ^= xor;
            result[i] = BitConverter.UInt64BitsToDouble(previousBits);
        }

        return result;
    }

    public byte[] CompressTimeStamps(long[] timestamps)
    {
        if (timestamps == null || timestamps.Length == 0) return Array.Empty<byte>();

        var writer = new BitWriter(timestamps.Length + 16);
        writer.WriteBits((ulong)timestamps.Length, 32);
        writer.WriteBits((ulong)timestamps[0], 64);
        if (timestamps.Length == 1) return writer.ToArray();

        // The first delta is stored at full width; deltas between arbitrary epochs do not
        // reliably fit the 32 bits the previous encoder reserved.
        long previousDelta = timestamps[1] - timestamps[0];
        writer.WriteBits((ulong)previousDelta, 64);

        for (int i = 2; i < timestamps.Length; i++)
        {
            long delta = timestamps[i] - timestamps[i - 1];
            long deltaOfDelta = delta - previousDelta;
            previousDelta = delta;

            if (deltaOfDelta == 0)
            {
                writer.WriteBit(false); // '0'
            }
            else if (deltaOfDelta is >= -63 and <= 64)
            {
                writer.WriteBits(0b10, 2);
                writer.WriteBits((ulong)(deltaOfDelta + 63), 7);
            }
            else if (deltaOfDelta is >= -255 and <= 256)
            {
                writer.WriteBits(0b110, 3);
                writer.WriteBits((ulong)(deltaOfDelta + 255), 9);
            }
            else if (deltaOfDelta is >= -2047 and <= 2048)
            {
                writer.WriteBits(0b1110, 4);
                writer.WriteBits((ulong)(deltaOfDelta + 2047), 12);
            }
            else
            {
                // Full 64-bit escape. Truncating to 32 bits here corrupted large epoch jumps.
                writer.WriteBits(0b1111, 4);
                writer.WriteBits((ulong)deltaOfDelta, 64);
            }
        }

        return writer.ToArray();
    }

    public long[] DecompressTimeStamps(byte[] compressedBytes)
    {
        if (compressedBytes == null || compressedBytes.Length < 4) return Array.Empty<long>();

        var reader = new BitReader(compressedBytes);
        int count = (int)reader.ReadBits(32);
        if (count <= 0) return Array.Empty<long>();

        var result = new long[count];
        result[0] = (long)reader.ReadBits(64);
        if (count == 1) return result;

        long previousDelta = (long)reader.ReadBits(64);
        result[1] = result[0] + previousDelta;

        for (int i = 2; i < count; i++)
        {
            long deltaOfDelta;

            if (!reader.ReadBit()) deltaOfDelta = 0;
            else if (!reader.ReadBit()) deltaOfDelta = (long)reader.ReadBits(7) - 63;
            else if (!reader.ReadBit()) deltaOfDelta = (long)reader.ReadBits(9) - 255;
            else if (!reader.ReadBit()) deltaOfDelta = (long)reader.ReadBits(12) - 2047;
            else deltaOfDelta = (long)reader.ReadBits(64);

            previousDelta += deltaOfDelta;
            result[i] = result[i - 1] + previousDelta;
        }

        return result;
    }
}
