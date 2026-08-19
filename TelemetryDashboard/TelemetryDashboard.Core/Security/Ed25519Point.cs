using System;
using System.Numerics;

namespace TelemetryDashboard.Core.Security;

/// <summary>
/// A point on the twisted Edwards curve -x² + y² = 1 + d·x²y² over GF(2²⁵⁵ - 19),
/// held in extended homogeneous coordinates (X : Y : Z : T) with x = X/Z, y = Y/Z, T = XY/Z.
/// </summary>
/// <remarks>
/// Extended coordinates keep group addition free of modular inversion; only the final
/// encoding inverts Z. The previous affine implementation performed two modular
/// inversions per point addition, which made a single signature hundreds of times slower.
/// </remarks>
internal readonly struct Ed25519Point
{
    /// <summary>Field prime 2²⁵⁵ - 19.</summary>
    internal static readonly BigInteger P = (BigInteger.One << 255) - 19;

    /// <summary>Curve constant d = -121665 / 121666 mod p.</summary>
    internal static readonly BigInteger D = Mod(-121665 * ModInverse(121666));

    private static readonly BigInteger D2 = Mod(D * 2);

    /// <summary>Square root of -1, used when recovering x from y.</summary>
    private static readonly BigInteger SqrtMinusOne = BigInteger.ModPow(2, (P - 1) / 4, P);

    internal static readonly Ed25519Point Identity = new(0, 1, 1, 0);

    /// <summary>Group generator B, the point with y = 4/5 and even x.</summary>
    internal static readonly Ed25519Point Base = CreateBasePoint();

    private readonly BigInteger _x;
    private readonly BigInteger _y;
    private readonly BigInteger _z;
    private readonly BigInteger _t;

    private Ed25519Point(BigInteger x, BigInteger y, BigInteger z, BigInteger t)
    {
        _x = x;
        _y = y;
        _z = z;
        _t = t;
    }

    private static Ed25519Point CreateBasePoint()
    {
        BigInteger y = Mod(4 * ModInverse(5));
        BigInteger x = RecoverX(y, 0) ?? throw new InvalidOperationException("Ed25519 base point is not on the curve.");
        return new Ed25519Point(x, y, BigInteger.One, Mod(x * y));
    }

    internal Ed25519Point Add(in Ed25519Point other)
    {
        BigInteger a = Mod((_y - _x) * (other._y - other._x));
        BigInteger b = Mod((_y + _x) * (other._y + other._x));
        BigInteger c = Mod(_t * D2 * other._t);
        BigInteger d = Mod(_z * 2 * other._z);
        BigInteger e = b - a;
        BigInteger f = d - c;
        BigInteger g = d + c;
        BigInteger h = b + a;

        return new Ed25519Point(Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
    }

    internal Ed25519Point Double()
    {
        BigInteger a = Mod(_x * _x);
        BigInteger b = Mod(_y * _y);
        BigInteger c = Mod(2 * _z * _z);
        BigInteger d = Mod(-a); // curve parameter a = -1
        BigInteger e = Mod((_x + _y) * (_x + _y) - a - b);
        BigInteger g = d + b;
        BigInteger f = g - c;
        BigInteger h = d - b;

        return new Ed25519Point(Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
    }

    /// <summary>Computes [scalar]this by binary double-and-add.</summary>
    internal Ed25519Point Multiply(BigInteger scalar)
    {
        Ed25519Point result = Identity;
        Ed25519Point addend = this;

        while (scalar > 0)
        {
            if (!scalar.IsEven)
            {
                result = result.Add(addend);
            }
            addend = addend.Double();
            scalar >>= 1;
        }

        return result;
    }

    /// <summary>Encodes the point as 32 little-endian bytes of y with the low bit of x in bit 255.</summary>
    internal byte[] Encode()
    {
        BigInteger zInverse = ModInverse(_z);
        BigInteger x = Mod(_x * zInverse);
        BigInteger y = Mod(_y * zInverse);

        byte[] encoded = EncodeLittleEndian(y);
        if (!x.IsEven)
        {
            encoded[31] |= 0x80;
        }
        return encoded;
    }

    /// <summary>Decodes a 32-byte point encoding, or null when it does not describe a curve point.</summary>
    internal static Ed25519Point? Decode(byte[] encoded)
    {
        if (encoded is not { Length: 32 }) return null;

        var copy = (byte[])encoded.Clone();
        int signBit = (copy[31] & 0x80) != 0 ? 1 : 0;
        copy[31] &= 0x7F;

        BigInteger y = DecodeLittleEndian(copy);
        if (y >= P) return null; // non-canonical encoding

        BigInteger? x = RecoverX(y, signBit);
        if (x is null) return null;

        return new Ed25519Point(x.Value, y, BigInteger.One, Mod(x.Value * y));
    }

    /// <summary>
    /// Solves x² = (y² - 1) / (d·y² + 1) and selects the root whose low bit matches
    /// <paramref name="signBit"/>. Returns null when no square root exists.
    /// </summary>
    private static BigInteger? RecoverX(BigInteger y, int signBit)
    {
        BigInteger u = Mod(y * y - 1);
        BigInteger v = Mod(D * y * y + 1);

        // Candidate root x = u·v³·(u·v⁷)^((p-5)/8)
        BigInteger v3 = Mod(v * v * v);
        BigInteger v7 = Mod(v3 * v3 * v);
        BigInteger x = Mod(u * v3 * BigInteger.ModPow(Mod(u * v7), (P - 5) / 8, P));

        BigInteger check = Mod(v * x * x);
        if (check == Mod(u))
        {
            // already the correct root
        }
        else if (check == Mod(-u))
        {
            x = Mod(x * SqrtMinusOne);
        }
        else
        {
            return null; // y does not correspond to any curve point
        }

        if (x == 0 && signBit == 1) return null; // non-canonical: -0 is not encodable
        if ((x.IsEven ? 0 : 1) != signBit) x = Mod(-x);

        return x;
    }

    internal static BigInteger Mod(BigInteger value)
    {
        BigInteger remainder = value % P;
        return remainder.Sign < 0 ? remainder + P : remainder;
    }

    private static BigInteger ModInverse(BigInteger value) => BigInteger.ModPow(Mod(value), P - 2, P);

    internal static BigInteger DecodeLittleEndian(byte[] bytes) =>
        new(bytes, isUnsigned: true, isBigEndian: false);

    internal static byte[] EncodeLittleEndian(BigInteger value)
    {
        byte[] raw = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        var result = new byte[32];
        Buffer.BlockCopy(raw, 0, result, 0, Math.Min(raw.Length, 32));
        return result;
    }
}
