using System.Buffers;
using System.Text;

namespace TelemetryDashboard.Core.Parsers;

public static class XorChecksum
{
    /// <summary>
    /// XOR checksum of the UTF-8 bytes these characters represent.
    /// </summary>
    /// <remarks>
    /// The checksum covers what goes on the wire, which is bytes. The firmware header this project
    /// generates says so in one line — <c>cs ^= ((const uint8_t*)(b))[i]</c> — and a device has no
    /// notion of a UTF-16 char to fold in instead.
    /// <para>
    /// This used to be <c>checksum ^= (byte)span[i]</c>, truncating each char to its low byte. For
    /// ASCII that is the same arithmetic, which is why it survived: every frame anyone had looked
    /// at was ASCII. A degree sign is not. U+00B0 truncates to one byte 0xB0 while the wire carries
    /// two, 0xC2 0xB0, so the two sides computed different checksums and the frame was rejected as
    /// corrupt — silently, because a failed checksum is indistinguishable from line noise. The
    /// default profile ships a channel whose unit is °C, so this was reachable by selecting it.
    /// </para>
    /// <para>
    /// The ASCII path is unchanged and still allocation-free; the encoding below runs only once a
    /// character above U+007F appears, and borrows its buffer rather than allocating one.
    /// </para>
    /// </remarks>
    public static byte Calculate(ReadOnlySpan<char> span)
    {
        byte checksum = 0;
        int i = 0;

        for (; i < span.Length; i++)
        {
            if (span[i] > 0x7F) break;
            checksum ^= (byte)span[i];
        }

        if (i == span.Length) return checksum;

        ReadOnlySpan<char> rest = span[i..];
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(rest));
        try
        {
            int written = Encoding.UTF8.GetBytes(rest, buffer);
            return (byte)(checksum ^ Calculate(buffer.AsSpan(0, written)));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static byte Calculate(ReadOnlySpan<byte> span)
    {
        byte checksum = 0;
        for (int i = 0; i < span.Length; i++)
        {
            checksum ^= span[i];
        }
        return checksum;
    }

    public static byte CalculateSpan(ReadOnlySpan<char> span) => Calculate(span);
    public static byte CalculateSpan(ReadOnlySpan<byte> span) => Calculate(span);

    /// <summary>
    /// Validates payload format "$[TAG],...*[XOR_HEX]".
    /// Returns true if valid XOR hex match.
    /// </summary>
    public static bool ValidateSpan(ReadOnlySpan<char> rawLine, out ReadOnlySpan<char> contentSpan)
    {
        contentSpan = ReadOnlySpan<char>.Empty;
        if (rawLine.IsEmpty) return false;

        int starIdx = rawLine.LastIndexOf('*');
        if (starIdx < 0 || starIdx + 2 >= rawLine.Length) return false;

        int startIdx = rawLine.StartsWith("$") ? 1 : 0;
        contentSpan = rawLine.Slice(startIdx, starIdx - startIdx);

        ReadOnlySpan<char> hexSpan = rawLine.Slice(starIdx + 1, 2);
        if (byte.TryParse(hexSpan, System.Globalization.NumberStyles.HexNumber, null, out byte expectedChecksum))
        {
            byte computed = Calculate(contentSpan);
            return computed == expectedChecksum;
        }

        return false;
    }

    public static string AppendChecksum(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return "$*00\r\n";
        ReadOnlySpan<char> span = payload.AsSpan().TrimStart('$');
        byte cs = Calculate(span);
        return $"${span}*{cs:X2}\r\n";
    }
}
