using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Collections;

/// <summary>
/// Most-significant-bit-first bit writer backed by a growable byte list.
/// Used by the Gorilla time-series codec to emit sub-byte aligned fields.
/// </summary>
public sealed class BitWriter
{
    private readonly List<byte> _bytes;
    private byte _current;
    private int _bitsUsed;

    public BitWriter(int initialCapacityBytes = 256)
    {
        _bytes = new List<byte>(Math.Max(16, initialCapacityBytes));
    }

    /// <summary>Total number of bits written so far.</summary>
    public long BitLength => (long)_bytes.Count * 8 + _bitsUsed;

    public void WriteBit(bool bit)
    {
        if (bit)
        {
            _current |= (byte)(1 << (7 - _bitsUsed));
        }

        if (++_bitsUsed == 8)
        {
            _bytes.Add(_current);
            _current = 0;
            _bitsUsed = 0;
        }
    }

    /// <summary>
    /// Writes the low <paramref name="count"/> bits of <paramref name="value"/>, most significant first.
    /// </summary>
    public void WriteBits(ulong value, int count)
    {
        if (count < 0 || count > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Bit count must be within 0..64.");
        }

        for (int i = count - 1; i >= 0; i--)
        {
            WriteBit(((value >> i) & 1UL) != 0UL);
        }
    }

    /// <summary>Flushes any partial byte and returns the encoded buffer.</summary>
    public byte[] ToArray()
    {
        if (_bitsUsed == 0)
        {
            return _bytes.ToArray();
        }

        var result = new byte[_bytes.Count + 1];
        _bytes.CopyTo(result);
        result[^1] = _current;
        return result;
    }
}

/// <summary>
/// Most-significant-bit-first bit reader. Reading past the end throws rather than
/// yielding zero bits, so a truncated or corrupted buffer surfaces immediately
/// instead of silently decoding into wrong values.
/// </summary>
public sealed class BitReader
{
    private readonly byte[] _data;
    private int _bytePosition;
    private int _bitPosition;

    public BitReader(byte[] data)
    {
        _data = data ?? Array.Empty<byte>();
    }

    /// <summary>Number of bits still available to read.</summary>
    public long BitsRemaining => (long)(_data.Length - _bytePosition) * 8 - _bitPosition;

    public bool ReadBit()
    {
        if (_bytePosition >= _data.Length)
        {
            throw new InvalidDataException("Bit stream exhausted: compressed buffer is truncated or corrupted.");
        }

        bool bit = (_data[_bytePosition] & (1 << (7 - _bitPosition))) != 0;

        if (++_bitPosition == 8)
        {
            _bitPosition = 0;
            _bytePosition++;
        }

        return bit;
    }

    public ulong ReadBits(int count)
    {
        if (count < 0 || count > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Bit count must be within 0..64.");
        }

        ulong value = 0;
        for (int i = 0; i < count; i++)
        {
            value = (value << 1) | (ReadBit() ? 1UL : 0UL);
        }
        return value;
    }
}
